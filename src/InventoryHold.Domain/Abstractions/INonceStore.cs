namespace InventoryHold.Domain.Abstractions;

public interface INonceStore
{
    /// <summary>Records the nonce for timeToLive. Returns false if this client has already used it.</summary>
    Task<bool> TryRememberAsync(string clientId, string nonce, TimeSpan timeToLive, CancellationToken cancellationToken);
}
