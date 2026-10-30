using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace InventoryHold.Infrastructure.Mongo;

internal static class MongoCollections
{
    public const string Products = "products";
    public const string Holds = "holds";
}

/// <summary>Creates indexes and seeds products before the API starts accepting requests.</summary>
internal sealed class MongoInitializer(IMongoDatabase database, ILogger<MongoInitializer> logger) : IHostedService
{
    private static readonly ProductDocument[] SeedProducts =
    [
        new() { Sku = "KB-MECH-01", Name = "Mechanical Keyboard", Available = 25 },
        new() { Sku = "MS-WL-02", Name = "Wireless Mouse", Available = 50 },
        new() { Sku = "MN-27-4K", Name = "27-inch 4K Monitor", Available = 10 },
        new() { Sku = "HS-NC-03", Name = "Noise-Cancelling Headset", Available = 15 },
        new() { Sku = "DK-USBC-04", Name = "USB-C Docking Station", Available = 30 },
        new() { Sku = "CM-4K-05", Name = "4K Webcam", Available = 3 }
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

        // $setOnInsert means a restart adds missing products but never resets live stock.
        var products = database.GetCollection<ProductDocument>(MongoCollections.Products);
        var upserts = SeedProducts.Select(seed => new UpdateOneModel<ProductDocument>(
            Builders<ProductDocument>.Filter.Eq(p => p.Sku, seed.Sku),
            Builders<ProductDocument>.Update
                .SetOnInsert(p => p.Name, seed.Name)
                .SetOnInsert(p => p.Available, seed.Available)
                .SetOnInsert(p => p.Held, 0))
        { IsUpsert = true });

        var result = await products.BulkWriteAsync(upserts, cancellationToken: cancellationToken);
        logger.LogInformation("MongoDB ready; seeded {Inserted} new product(s)", result.Upserts.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
