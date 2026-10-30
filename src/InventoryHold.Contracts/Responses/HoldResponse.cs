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

public sealed record HoldItemResponse(string Sku, string ProductName, int Quantity);
