using InventoryHold.Domain.Repositories;
using MongoDB.Driver;

namespace InventoryHold.Infrastructure.Mongo;

internal sealed class MongoInventoryUnitRepository(IMongoDatabase database) : IInventoryUnitRepository
{
    private readonly IMongoCollection<InventoryUnitDocument> _units =
        database.GetCollection<InventoryUnitDocument>(MongoCollections.InventoryUnits);

    public async Task<IReadOnlyList<string>> ClaimAsync(string sku, string holdId, int quantity, CancellationToken cancellationToken)
    {
        // Each claim is atomic on one document, so no two holds can ever get the same serial number.
        var serials = new List<string>(quantity);
        for (var i = 0; i < quantity; i++)
        {
            var unit = await _units.FindOneAndUpdateAsync(
                u => u.Sku == sku && u.HoldId == null,
                Builders<InventoryUnitDocument>.Update.Set(u => u.HoldId, holdId),
                new FindOneAndUpdateOptions<InventoryUnitDocument>
                {
                    Sort = Builders<InventoryUnitDocument>.Sort.Ascending(u => u.SerialNumber)
                },
                cancellationToken);

            if (unit is null)
                break;
            serials.Add(unit.SerialNumber);
        }
        return serials;
    }

    public Task ReleaseAsync(string holdId, CancellationToken cancellationToken) =>
        _units.UpdateManyAsync(
            u => u.HoldId == holdId,
            Builders<InventoryUnitDocument>.Update.Set(u => u.HoldId, null),
            cancellationToken: cancellationToken);
}
