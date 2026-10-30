using System.Text.Json.Serialization;
using InventoryHold.Domain.Security;
using InventoryHold.Domain.Services;
using InventoryHold.Infrastructure;
using InventoryHold.WebApi.BackgroundServices;
using InventoryHold.WebApi.ErrorHandling;
using InventoryHold.WebApi.Security;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<HoldOptions>()
    .Bind(builder.Configuration.GetSection(HoldOptions.SectionName))
    .Validate(o => o.DefaultDurationMinutes > 0, "Holds:DefaultDurationMinutes must be positive.")
    .Validate(o => o.SweepIntervalSeconds > 0, "Holds:SweepIntervalSeconds must be positive.")
    .Validate(o => o.SweepBatchSize > 0, "Holds:SweepBatchSize must be positive.")
    .Validate(o => o.ListLimit > 0, "Holds:ListLimit must be positive.")
    .Validate(o => o.ShippingCutoffHourUtc is >= 0 and <= 23, "Holds:ShippingCutoffHourUtc must be 0 to 23.")
    .ValidateOnStart();
builder.Services.AddScoped<IHoldService, HoldService>();

builder.Services.AddOptions<HmacOptions>()
    .Bind(builder.Configuration.GetSection(HmacOptions.SectionName))
    .Validate(o => !o.Enabled || o.HasUsableClients,
        $"HMAC is enabled, so at least one client secret is required, and each must be {HmacOptions.MinimumSecretLength}+ characters. " +
        "Set Security:Hmac:Clients (HMAC_WEB_SECRET and HMAC_CHECKOUT_SECRET under docker-compose), or set Security:Hmac:Enabled to false.")
    .Validate(o => o.TimestampToleranceSeconds > 0, "Security:Hmac:TimestampToleranceSeconds must be positive.")
    .Validate(o => o.MaxBodyBytes > 0, "Security:Hmac:MaxBodyBytes must be positive.")
    .ValidateOnStart();
builder.Services.AddSingleton<HmacSignatureVerifier>();

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<HoldExpirySweeper>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<HmacAuthenticationMiddleware>();

app.MapOpenApi();
app.MapHealthChecks("/health");
app.MapControllers();

app.Run();
