using InventoryHold.Domain.Entities;

namespace InventoryHold.Domain.Repositories;

public interface IProductRepository
{
    Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Product>> GetBySkusAsync(IReadOnlyCollection<string> skus, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically moves quantity from available to held, only if enough is available.
    /// Returns false, changing nothing, if stock is insufficient.
    /// </summary>
    Task<bool> TryReserveAsync(string sku, int quantity, CancellationToken cancellationToken);

    /// <summary>Atomically moves quantity from held back to available.</summary>
    Task RestoreAsync(string sku, int quantity, CancellationToken cancellationToken);
}
