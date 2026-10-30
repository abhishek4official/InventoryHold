namespace InventoryHold.Domain.Entities;

/// <summary>One physical item of a serial-tracked product. A null HoldId means it is free.</summary>
public sealed record InventoryUnit(
    string SerialNumber,
    string Sku,
    string? HoldId,
    string LotNumber,
    string Condition,
    DateTimeOffset? ReceivedAt,
    DateTimeOffset? LastUpdatedAt);
