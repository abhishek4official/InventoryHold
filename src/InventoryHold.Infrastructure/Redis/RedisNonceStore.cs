using InventoryHold.Domain.Abstractions;
using InventoryHold.Infrastructure.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace InventoryHold.Infrastructure.Redis;

/// <summary>Failures propagate; the verifier decides what an outage of this store means.</summary>
internal sealed class RedisNonceStore(IConnectionMultiplexer redis, IOptions<RedisOptions> options) : INonceStore
{
    private readonly IDatabase _database = redis.GetDatabase();
    private readonly string _keyPrefix = $"{options.Value.KeyPrefix}nonce:";

    public Task<bool> TryRememberAsync(string clientId, string nonce, TimeSpan timeToLive, CancellationToken cancellationToken) =>
        _database.StringSetAsync($"{_keyPrefix}{clientId}:{nonce}", "1", timeToLive, When.NotExists);
}
