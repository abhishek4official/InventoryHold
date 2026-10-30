using System.Globalization;
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
    public string PartNumber { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Category { get; set; } = "";
    public string WarehouseLocation { get; set; } = "";
    public string UnitOfMeasure { get; set; } = "EA";
    public int ReorderPoint { get; set; }
    public int HandlingDays { get; set; } = 1;
    public bool IsSerialized { get; set; }

    public Product ToEntity() => new(
        Sku, Name, Available, Held, PartNumber, Manufacturer, Category, WarehouseLocation,
        UnitOfMeasure, ReorderPoint, HandlingDays, IsSerialized);
}

/// <summary>One physical unit of a serial-tracked product. A null HoldId means it is free.</summary>
internal sealed class InventoryUnitDocument
{
    [BsonId] public string SerialNumber { get; set; } = "";
    public string Sku { get; set; } = "";
    public string? HoldId { get; set; }
}

internal sealed class HoldLineDocument
{
    public string Sku { get; set; } = "";
    public string ProductName { get; set; } = "";
    public int Quantity { get; set; }
    public string PartNumber { get; set; } = "";

    /// <summary>An ISO date string, so it reads naturally in the database and carries no time zone.</summary>
    public string? ApproxShipDate { get; set; }

    public List<string> SerialNumbers { get; set; } = [];
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
            .Select(l => new HoldLineDocument
            {
                Sku = l.Sku,
                ProductName = l.ProductName,
                Quantity = l.Quantity,
                PartNumber = l.PartNumber,
                ApproxShipDate = l.ApproxShipDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                SerialNumbers = l.SerialNumbers?.ToList() ?? []
            })
            .ToList(),
        CreatedAt = hold.CreatedAt.UtcDateTime,
        ExpiresAt = hold.ExpiresAt.UtcDateTime,
        ClosedAt = hold.ClosedAt?.UtcDateTime
    };

    public Hold ToEntity() => new(
        Id,
        CustomerId,
        Status,
        Lines.Select(l => new HoldLine(
            l.Sku,
            l.ProductName,
            l.Quantity,
            l.PartNumber,
            l.ApproxShipDate is null ? null : DateOnly.ParseExact(l.ApproxShipDate, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            l.SerialNumbers)).ToList(),
        ToOffset(CreatedAt),
        ToOffset(ExpiresAt),
        ClosedAt is { } closedAt ? ToOffset(closedAt) : null);

    private static DateTimeOffset ToOffset(DateTime utc) => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
}
