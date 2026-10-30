using System.Text.Json;
using System.Text.Json.Serialization;
using InventoryHold.Contracts.Responses;
using InventoryHold.Domain.Abstractions;
using InventoryHold.Infrastructure.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace InventoryHold.Infrastructure.Redis;

/// <summary>Failures propagate; HoldService decides that the cache is optional and falls back.</summary>
internal sealed class RedisInventoryCache(IConnectionMultiplexer redis, IOptions<RedisOptions> options) : IInventoryCache
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IDatabase _database = redis.GetDatabase();
    private readonly string _inventoryKey = $"{options.Value.KeyPrefix}inventory";
    private readonly string _holdKeyPrefix = $"{options.Value.KeyPrefix}hold:";
    private readonly TimeSpan _inventoryTtl = TimeSpan.FromSeconds(options.Value.InventoryTtlSeconds);

    public Task<IReadOnlyList<InventoryItemResponse>?> GetInventoryAsync(CancellationToken cancellationToken) =>
        ReadAsync<IReadOnlyList<InventoryItemResponse>>(_inventoryKey);

    public Task SetInventoryAsync(IReadOnlyList<InventoryItemResponse> inventory, CancellationToken cancellationToken) =>
        _database.StringSetAsync(_inventoryKey, JsonSerializer.Serialize(inventory, Json), _inventoryTtl);

    public Task InvalidateInventoryAsync(CancellationToken cancellationToken) =>
        _database.KeyDeleteAsync(_inventoryKey);

    public Task<HoldResponse?> GetHoldAsync(string holdId, CancellationToken cancellationToken) =>
        ReadAsync<HoldResponse>(_holdKeyPrefix + holdId);

    public Task SetHoldAsync(HoldResponse hold, TimeSpan timeToLive, CancellationToken cancellationToken) =>
        _database.StringSetAsync(_holdKeyPrefix + hold.Id, JsonSerializer.Serialize(hold, Json), timeToLive);

    public Task InvalidateHoldAsync(string holdId, CancellationToken cancellationToken) =>
        _database.KeyDeleteAsync(_holdKeyPrefix + holdId);

    private async Task<T?> ReadAsync<T>(string key) where T : class
    {
        var value = await _database.StringGetAsync(key);
        return value.HasValue ? JsonSerializer.Deserialize<T>(value.ToString(), Json) : null;
    }
}
