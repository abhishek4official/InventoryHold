using InventoryHold.Domain.Entities;
using InventoryHold.Domain.Repositories;
using MongoDB.Driver;

namespace InventoryHold.Infrastructure.Mongo;

internal sealed class MongoProductRepository(IMongoDatabase database) : IProductRepository
{
    private readonly IMongoCollection<ProductDocument> _products =
        database.GetCollection<ProductDocument>(MongoCollections.Products);

    public async Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken)
    {
        var documents = await _products.Find(FilterDefinition<ProductDocument>.Empty)
            .SortBy(p => p.Name)
            .ToListAsync(cancellationToken);
        return documents.Select(d => d.ToEntity()).ToList();
    }

    public async Task<IReadOnlyList<Product>> GetBySkusAsync(IReadOnlyCollection<string> skus, CancellationToken cancellationToken)
    {
        var documents = await _products.Find(Builders<ProductDocument>.Filter.In(p => p.Sku, skus))
            .ToListAsync(cancellationToken);
        return documents.Select(d => d.ToEntity()).ToList();
    }

    public async Task<bool> TryReserveAsync(string sku, int quantity, CancellationToken cancellationToken)
    {
        // The filter and the $inc are one atomic operation on one document, so two
        // concurrent requests can never both pass the check against the same units.
        var result = await _products.UpdateOneAsync(
            p => p.Sku == sku && p.Available >= quantity,
            Builders<ProductDocument>.Update.Inc(p => p.Available, -quantity).Inc(p => p.Held, quantity).CurrentDate(p => p.LastUpdatedAt),
            cancellationToken: cancellationToken);
        return result.ModifiedCount == 1;
    }

    public Task RestoreAsync(string sku, int quantity, CancellationToken cancellationToken) =>
        _products.UpdateOneAsync(
            p => p.Sku == sku,
            Builders<ProductDocument>.Update.Inc(p => p.Available, quantity).Inc(p => p.Held, -quantity).CurrentDate(p => p.LastUpdatedAt),
            cancellationToken: cancellationToken);
}
