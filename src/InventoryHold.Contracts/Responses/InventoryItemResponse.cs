namespace InventoryHold.Contracts.Responses;

/// <summary>ApproxShipDate is what an order placed now could expect; null when nothing is available to ship.</summary>
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
    DateOnly? ApproxShipDate);
