using InventoryHold.Contracts.Enums;
using InventoryHold.Domain.Entities;

namespace InventoryHold.Domain.Repositories;

public interface IHoldRepository
{
    Task InsertAsync(Hold hold, CancellationToken cancellationToken);

    Task<Hold?> GetByIdAsync(string holdId, CancellationToken cancellationToken);

    /// <summary>Most recent first. A null status returns holds of every status.</summary>
    Task<IReadOnlyList<Hold>> ListAsync(HoldStatus? status, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<Hold>> GetDueAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically moves an Active hold to the given closed status. Returns the updated hold, or null
    /// if it was not Active. Of any number of concurrent callers only one gets a hold back, and that
    /// caller alone restores stock and publishes the event.
    /// </summary>
    Task<Hold?> TryCloseAsync(string holdId, HoldStatus closedStatus, DateTimeOffset closedAt, CancellationToken cancellationToken);
}
