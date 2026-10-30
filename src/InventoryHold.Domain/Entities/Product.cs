namespace InventoryHold.Domain.Entities;

/// <summary>
/// One stock-keeping unit. Sku is this business's identifier; PartNumber is the manufacturer's, and
/// several SKUs can share one. HandlingDays is the business days from order to dispatch.
/// </summary>
public sealed record Product(
    string Sku,
    string Name,
    int Available,
    int Held,
    string PartNumber = "",
    string Manufacturer = "",
    string Category = "",
    string WarehouseLocation = "",
    string UnitOfMeasure = "EA",
    int ReorderPoint = 0,
    int HandlingDays = 1,
    bool IsSerialized = false);
