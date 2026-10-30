using InventoryHold.Contracts.Enums;
using InventoryHold.Domain.Entities;
using InventoryHold.Domain.Repositories;
using MongoDB.Driver;

namespace InventoryHold.Infrastructure.Mongo;

internal sealed class MongoHoldRepository(IMongoDatabase database) : IHoldRepository
{
    private readonly IMongoCollection<HoldDocument> _holds =
        database.GetCollection<HoldDocument>(MongoCollections.Holds);

    public Task InsertAsync(Hold hold, CancellationToken cancellationToken) =>
        _holds.InsertOneAsync(HoldDocument.FromEntity(hold), cancellationToken: cancellationToken);

    public async Task<Hold?> GetByIdAsync(string holdId, CancellationToken cancellationToken)
    {
        var document = await _holds.Find(h => h.Id == holdId).FirstOrDefaultAsync(cancellationToken);
        return document?.ToEntity();
    }

    public async Task<IReadOnlyList<Hold>> ListAsync(HoldStatus? status, int limit, CancellationToken cancellationToken)
    {
        var filter = status is { } wanted
            ? Builders<HoldDocument>.Filter.Eq(h => h.Status, wanted)
            : FilterDefinition<HoldDocument>.Empty;

        var documents = await _holds.Find(filter)
            .SortByDescending(h => h.CreatedAt)
            .Limit(limit)
            .ToListAsync(cancellationToken);
        return documents.Select(d => d.ToEntity()).ToList();
    }

    public async Task<IReadOnlyList<Hold>> GetDueAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken)
    {
        var cutoff = now.UtcDateTime;
        var documents = await _holds.Find(h => h.Status == HoldStatus.Active && h.ExpiresAt <= cutoff)
            .SortBy(h => h.ExpiresAt)
            .Limit(limit)
            .ToListAsync(cancellationToken);
        return documents.Select(d => d.ToEntity()).ToList();
    }

    public async Task<Hold?> TryCloseAsync(string holdId, HoldStatus closedStatus, DateTimeOffset closedAt, CancellationToken cancellationToken)
    {
        var document = await _holds.FindOneAndUpdateAsync(
            h => h.Id == holdId && h.Status == HoldStatus.Active,
            Builders<HoldDocument>.Update.Set(h => h.Status, closedStatus).Set(h => h.ClosedAt, closedAt.UtcDateTime),
            new FindOneAndUpdateOptions<HoldDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken);
        return document?.ToEntity();
    }
}
