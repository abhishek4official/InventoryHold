using InventoryHold.Contracts.Responses;

namespace InventoryHold.Domain.Abstractions;

public interface IInventoryCache
{
    Task<IReadOnlyList<InventoryItemResponse>?> GetInventoryAsync(CancellationToken cancellationToken);

    Task SetInventoryAsync(IReadOnlyList<InventoryItemResponse> inventory, CancellationToken cancellationToken);

    Task InvalidateInventoryAsync(CancellationToken cancellationToken);

    Task<HoldResponse?> GetHoldAsync(string holdId, CancellationToken cancellationToken);

    Task SetHoldAsync(HoldResponse hold, TimeSpan timeToLive, CancellationToken cancellationToken);

    Task InvalidateHoldAsync(string holdId, CancellationToken cancellationToken);
}
