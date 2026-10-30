using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace InventoryHold.Infrastructure.Mongo;

internal static class MongoCollections
{
    public const string Products = "products";
    public const string Holds = "holds";
    public const string InventoryUnits = "inventory_units";
}

/// <summary>Creates indexes and seeds the catalogue before the API starts accepting requests.</summary>
internal sealed class MongoInitializer(IMongoDatabase database, ILogger<MongoInitializer> logger) : IHostedService
{
    private static readonly ProductDocument[] SeedProducts =
    [
        // Sample data. Cheap accessories are counted only; higher-value items are tracked per unit by serial number.
        new() { Sku = "KB-MECH-01", Name = "Mechanical Keyboard", Available = 25, PartNumber = "KX-MK870-US", Manufacturer = "Keyworks", Category = "Peripherals", WarehouseLocation = "WH1-A-03-02", ReorderPoint = 5, HandlingDays = 1 },
        new() { Sku = "MS-WL-02", Name = "Wireless Mouse", Available = 50, PartNumber = "MW-2400-BLK", Manufacturer = "Pointline", Category = "Peripherals", WarehouseLocation = "WH1-A-03-05", ReorderPoint = 10, HandlingDays = 1 },
        new() { Sku = "MN-27-4K", Name = "27-inch 4K Monitor", Available = 10, PartNumber = "VD-27U4K-IPS", Manufacturer = "Viewdeck", Category = "Displays", WarehouseLocation = "WH1-C-01-01", ReorderPoint = 3, HandlingDays = 3, IsSerialized = true },
        new() { Sku = "HS-NC-03", Name = "Noise-Cancelling Headset", Available = 15, PartNumber = "AQ-HS700-NC", Manufacturer = "Aquatone", Category = "Audio", WarehouseLocation = "WH1-B-02-04", ReorderPoint = 4, HandlingDays = 2, IsSerialized = true },
        new() { Sku = "DK-USBC-04", Name = "USB-C Docking Station", Available = 30, PartNumber = "PL-DOCK12-C", Manufacturer = "Portlink", Category = "Accessories", WarehouseLocation = "WH1-B-04-01", ReorderPoint = 6, HandlingDays = 2, IsSerialized = true },
        new() { Sku = "CM-4K-05", Name = "4K Webcam", Available = 3, PartNumber = "LN-CAM4K-PRO", Manufacturer = "Lensora", Category = "Video", WarehouseLocation = "WH1-B-01-07", ReorderPoint = 3, HandlingDays = 1, IsSerialized = true }
    ];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var holds = database.GetCollection<HoldDocument>(MongoCollections.Holds);
        await holds.Indexes.CreateManyAsync(
        [
            // Serves the expiry sweep, and the status filter of the list endpoint.
            new CreateIndexModel<HoldDocument>(Builders<HoldDocument>.IndexKeys.Ascending(h => h.Status).Ascending(h => h.ExpiresAt)),
            new CreateIndexModel<HoldDocument>(Builders<HoldDocument>.IndexKeys.Ascending(h => h.Status).Descending(h => h.CreatedAt))
        ], cancellationToken);

        var units = database.GetCollection<InventoryUnitDocument>(MongoCollections.InventoryUnits);
        await units.Indexes.CreateManyAsync(
        [
            // Serves claiming a free unit in serial order, and freeing a hold's units.
            new CreateIndexModel<InventoryUnitDocument>(Builders<InventoryUnitDocument>.IndexKeys.Ascending(u => u.Sku).Ascending(u => u.HoldId).Ascending(u => u.SerialNumber)),
            new CreateIndexModel<InventoryUnitDocument>(Builders<InventoryUnitDocument>.IndexKeys.Ascending(u => u.HoldId))
        ], cancellationToken);

        // Catalogue details use $set, so a redeploy updates them. Stock uses $setOnInsert,
        // so a restart adds missing products but never resets live stock.
        var products = database.GetCollection<ProductDocument>(MongoCollections.Products);
        var upserts = SeedProducts.Select(seed => new UpdateOneModel<ProductDocument>(
            Builders<ProductDocument>.Filter.Eq(p => p.Sku, seed.Sku),
            Builders<ProductDocument>.Update
                .Set(p => p.Name, seed.Name)
                .Set(p => p.PartNumber, seed.PartNumber)
                .Set(p => p.Manufacturer, seed.Manufacturer)
                .Set(p => p.Category, seed.Category)
                .Set(p => p.WarehouseLocation, seed.WarehouseLocation)
                .Set(p => p.UnitOfMeasure, seed.UnitOfMeasure)
                .Set(p => p.ReorderPoint, seed.ReorderPoint)
                .Set(p => p.HandlingDays, seed.HandlingDays)
                .Set(p => p.IsSerialized, seed.IsSerialized)
                .SetOnInsert(p => p.Available, seed.Available)
                .SetOnInsert(p => p.Held, 0))
        { IsUpsert = true });

        var result = await products.BulkWriteAsync(upserts, cancellationToken: cancellationToken);

        var unitUpserts = SeedProducts.Where(p => p.IsSerialized).SelectMany(p => Enumerable.Range(1, p.Available).Select(n =>
            new UpdateOneModel<InventoryUnitDocument>(
                Builders<InventoryUnitDocument>.Filter.Eq(u => u.SerialNumber, $"{p.Sku}-{n:D5}"),
                Builders<InventoryUnitDocument>.Update.SetOnInsert(u => u.Sku, p.Sku).SetOnInsert(u => u.HoldId, null))
            { IsUpsert = true }));
        var unitResult = await units.BulkWriteAsync(unitUpserts, cancellationToken: cancellationToken);

        logger.LogInformation("MongoDB ready; seeded {Products} new product(s) and {Units} new serial-numbered unit(s)",
            result.Upserts.Count, unitResult.Upserts.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
