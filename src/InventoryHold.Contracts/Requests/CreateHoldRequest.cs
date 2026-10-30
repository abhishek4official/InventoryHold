namespace InventoryHold.Contracts.Requests;

public sealed record CreateHoldRequest(string CustomerId, IReadOnlyList<HoldItemRequest> Items);

public sealed record HoldItemRequest(string Sku, int Quantity);
