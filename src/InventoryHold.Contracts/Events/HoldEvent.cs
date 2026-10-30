using InventoryHold.Contracts.Responses;

namespace InventoryHold.Contracts.Events;

public static class HoldEventTypes
{
    public const string Created = "HoldCreated";
    public const string Released = "HoldReleased";
    public const string Expired = "HoldExpired";
}

/// <summary>
/// Self-contained lifecycle event: a consumer can act on it without calling back into this service.
/// EventId lets consumers de-duplicate, since delivery is at-least-once.
/// </summary>
public sealed record HoldEvent(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    string HoldId,
    string CustomerId,
    IReadOnlyList<HoldItemResponse> Items,
    DateTimeOffset ExpiresAt,
    int SchemaVersion = 1);
