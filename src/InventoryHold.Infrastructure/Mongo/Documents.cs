using InventoryHold.Contracts.Enums;
using InventoryHold.Domain.Entities;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace InventoryHold.Infrastructure.Mongo;

// Persistence shapes are kept out of the domain so entities carry no MongoDB attributes.

internal sealed class ProductDocument
{
    [BsonId] public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public int Available { get; set; }
    public int Held { get; set; }

    public Product ToEntity() => new(Sku, Name, Available, Held);
}

internal sealed class HoldLineDocument
{
    public string Sku { get; set; } = "";
    public string ProductName { get; set; } = "";
    public int Quantity { get; set; }
}

internal sealed class HoldDocument
{
    [BsonId] public string Id { get; set; } = "";
    public string CustomerId { get; set; } = "";

    [BsonRepresentation(BsonType.String)]
    public HoldStatus Status { get; set; }

    public List<HoldLineDocument> Lines { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    public static HoldDocument FromEntity(Hold hold) => new()
    {
        Id = hold.Id,
        CustomerId = hold.CustomerId,
        Status = hold.Status,
        Lines = hold.Lines
            .Select(l => new HoldLineDocument { Sku = l.Sku, ProductName = l.ProductName, Quantity = l.Quantity })
            .ToList(),
        CreatedAt = hold.CreatedAt.UtcDateTime,
        ExpiresAt = hold.ExpiresAt.UtcDateTime,
        ClosedAt = hold.ClosedAt?.UtcDateTime
    };

    public Hold ToEntity() => new(
        Id,
        CustomerId,
        Status,
        Lines.Select(l => new HoldLine(l.Sku, l.ProductName, l.Quantity)).ToList(),
        ToOffset(CreatedAt),
        ToOffset(ExpiresAt),
        ClosedAt is { } closedAt ? ToOffset(closedAt) : null);

    private static DateTimeOffset ToOffset(DateTime utc) => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
}
