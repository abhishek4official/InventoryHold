using InventoryHold.Contracts.Enums;

namespace InventoryHold.Contracts.Responses;

/// <summary>
/// One product. ApproxShipDate is what an order placed now could expect, or null when nothing is
/// available to ship. Units lists each physical item; it is empty for products tracked by quantity.
/// </summary>
public sealed record InventoryItemResponse(
    string Sku,
    string Name,
    string PartNumber,
    string Manufacturer,
    string Category,
    string WarehouseLocation,
    string UnitOfMeasure,
    int Available,
    int Held,
    int ReorderPoint,
    bool LowStock,
    bool IsSerialized,
    DateOnly? ApproxShipDate,
    DateTimeOffset? LastUpdatedAt,
    IReadOnlyList<InventoryUnitResponse> Units);

public sealed record InventoryUnitResponse(
    string SerialNumber,
    UnitStatus Status,
    string? HoldId,
    string LotNumber,
    string Condition,
    DateTimeOffset? ReceivedAt,
    DateTimeOffset? LastUpdatedAt);
