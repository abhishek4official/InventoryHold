using InventoryHold.Domain.Security;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace InventoryHold.WebApi.Security;

/// <summary>Requires a valid signature on /api. The health check and OpenAPI document stay open.</summary>
public sealed class HmacAuthenticationMiddleware(
    RequestDelegate next,
    HmacSignatureVerifier verifier,
    IOptions<HmacOptions> options,
    IProblemDetailsService problemDetails,
    ILogger<HmacAuthenticationMiddleware> logger)
{
    private readonly HmacOptions _options = options.Value;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.Enabled || !context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        var body = await ReadBodyAsync(context.Request);
        if (body is null)
        {
            await WriteProblemAsync(context, StatusCodes.Status413PayloadTooLarge, "Request too large",
                $"The request body must not exceed {_options.MaxBodyBytes} bytes.", "body_too_large");
            return;
        }

        var headers = context.Request.Headers;
        var failure = await verifier.VerifyAsync(new HmacRequest(
            context.Request.Method,
            // The target exactly as sent, because decoding could change what was signed.
            context.Features.GetRequiredFeature<IHttpRequestFeature>().RawTarget,
            headers["X-Client-Id"],
            headers["X-Timestamp"],
            headers["X-Nonce"],
            headers["X-Signature"],
            body), context.RequestAborted);

        if (failure is null)
        {
            await next(context);
            return;
        }

        logger.LogWarning("Rejected {Method} {Path} from client {ClientId}: {Failure}",
            context.Request.Method, context.Request.Path, (string?)headers["X-Client-Id"], failure);
        var (detail, code) = Describe(failure.Value);
        context.Response.Headers.WWWAuthenticate = "HMAC-SHA256";
        await WriteProblemAsync(context, StatusCodes.Status401Unauthorized, "Request not authenticated", detail, code);
    }

    /// <summary>Returns the body with the stream rewound for the controller, or null if it is too large.</summary>
    private async Task<byte[]?> ReadBodyAsync(HttpRequest request)
    {
        if (request.ContentLength > _options.MaxBodyBytes)
            return null;

        request.EnableBuffering();
        var buffer = new byte[_options.MaxBodyBytes + 1];
        var total = 0;
        int read;
        while (total < buffer.Length && (read = await request.Body.ReadAsync(buffer.AsMemory(total), request.HttpContext.RequestAborted)) > 0)
            total += read;

        if (total > _options.MaxBodyBytes)
            return null;

        request.Body.Position = 0;
        return buffer[..total];
    }

    private static (string Detail, string Code) Describe(HmacFailure failure) => failure switch
    {
        HmacFailure.MissingSignature => ("X-Client-Id, X-Timestamp, X-Nonce and X-Signature headers are required.", "missing_signature"),
        HmacFailure.StaleTimestamp => ("X-Timestamp is outside the allowed window. Check the client's clock.", "stale_timestamp"),
        HmacFailure.ReplayedRequest => ("This X-Nonce has already been used.", "replayed_request"),
        _ => ("The signature does not match this request.", "invalid_signature")
    };

    private Task WriteProblemAsync(HttpContext context, int status, string title, string detail, string code)
    {
        context.Response.StatusCode = status;
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        problem.Extensions["code"] = code;
        return problemDetails.WriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem }).AsTask();
    }
}
