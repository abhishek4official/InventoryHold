using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using InventoryHold.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InventoryHold.Domain.Security;

public enum HmacFailure
{
    MissingSignature,
    InvalidSignature,
    StaleTimestamp,
    ReplayedRequest
}

public sealed record HmacRequest(
    string Method,
    string PathAndQuery,
    string? ClientId,
    string? Timestamp,
    string? Nonce,
    string? Signature,
    ReadOnlyMemory<byte> Body);

/// <summary>
/// Proves a request came from a known client, was not altered, and is not a replay. The signature is
/// checked before the nonce, so that a caller without a secret cannot fill the nonce store.
/// </summary>
public sealed class HmacSignatureVerifier(
    IOptions<HmacOptions> options,
    INonceStore nonces,
    TimeProvider clock,
    ILogger<HmacSignatureVerifier> logger)
{
    // Unknown clients are still compared against something, so that they take as long as known ones.
    private static readonly string UnknownClientSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private readonly HmacOptions _options = options.Value;

    /// <summary>Returns null if the request is genuine, otherwise why it was rejected.</summary>
    public async Task<HmacFailure?> VerifyAsync(HmacRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.ClientId) || string.IsNullOrEmpty(request.Timestamp) ||
            string.IsNullOrEmpty(request.Nonce) || string.IsNullOrEmpty(request.Signature))
            return HmacFailure.MissingSignature;

        var known = _options.Clients.TryGetValue(request.ClientId, out var secret) && !string.IsNullOrEmpty(secret);
        var expected = Sign(known ? secret! : UnknownClientSecret, request.Method, request.PathAndQuery, request.Timestamp, request.Nonce, request.Body.Span);
        var matches = CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(request.Signature));
        if (!known || !matches)
            return HmacFailure.InvalidSignature;

        var tolerance = TimeSpan.FromSeconds(_options.TimestampToleranceSeconds);
        if (!long.TryParse(request.Timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) ||
            seconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds() ||
            (DateTimeOffset.FromUnixTimeSeconds(seconds) - clock.GetUtcNow()).Duration() > tolerance)
            return HmacFailure.StaleTimestamp;

        // Remembered for twice the window, which outlasts any timestamp that could still pass the check above.
        return await IsFirstUseAsync(request.ClientId, request.Nonce, tolerance * 2, cancellationToken)
            ? null
            : HmacFailure.ReplayedRequest;
    }

    public static string Sign(string secret, string method, string pathAndQuery, string timestamp, string nonce, ReadOnlySpan<byte> body)
    {
        var stringToSign = $"{method}\n{pathAndQuery}\n{timestamp}\n{nonce}\n{Convert.ToHexStringLower(SHA256.HashData(body))}";
        return Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(stringToSign)));
    }

    /// <summary>
    /// An outage of the nonce store must not take down checkout, so it fails open. The timestamp
    /// check still limits a replay to the tolerance window.
    /// </summary>
    private async Task<bool> IsFirstUseAsync(string clientId, string nonce, TimeSpan timeToLive, CancellationToken cancellationToken)
    {
        try
        {
            return await nonces.TryRememberAsync(clientId, nonce, timeToLive, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Nonce store unavailable; replay protection is limited to the timestamp window");
            return true;
        }
    }
}
