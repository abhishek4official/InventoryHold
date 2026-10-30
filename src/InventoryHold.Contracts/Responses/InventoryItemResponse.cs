namespace InventoryHold.Contracts.Responses;

public sealed record InventoryItemResponse(string Sku, string Name, int Available, int Held);
