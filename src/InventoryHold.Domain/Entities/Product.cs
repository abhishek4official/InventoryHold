namespace InventoryHold.Domain.Entities;

public sealed record Product(string Sku, string Name, int Available, int Held);
