using InventoryHold.Domain.Entities;

namespace InventoryHold.Domain.Repositories;

/// <summary>Individual serial-numbered units of the products tracked by serial.</summary>
public interface IInventoryUnitRepository
{
    /// <summary>Every unit, in serial order.</summary>
    Task<IReadOnlyList<InventoryUnit>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Atomically assigns up to quantity available units to the hold and returns their serial numbers.
    /// Returns fewer than requested if that many are not free.
    /// </summary>
    Task<IReadOnlyList<string>> ClaimAsync(string sku, string holdId, int quantity, CancellationToken cancellationToken);

    /// <summary>Returns every unit assigned to the hold. Safe to repeat.</summary>
    Task ReleaseAsync(string holdId, CancellationToken cancellationToken);
}
