using InventoryHold.Contracts.Enums;

namespace InventoryHold.Contracts.Responses;

public sealed record HoldResponse(
    string Id,
    string CustomerId,
    HoldStatus Status,
    IReadOnlyList<HoldItemResponse> Items,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ClosedAt);

/// <summary>SerialNumbers lists the exact units held; it is empty for products not tracked by serial.</summary>
public sealed record HoldItemResponse(
    string Sku,
    string ProductName,
    string PartNumber,
    int Quantity,
    DateOnly? ApproxShipDate,
    IReadOnlyList<string> SerialNumbers);
