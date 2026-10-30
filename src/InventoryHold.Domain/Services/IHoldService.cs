using InventoryHold.Contracts.Enums;
using InventoryHold.Contracts.Requests;
using InventoryHold.Contracts.Responses;

namespace InventoryHold.Domain.Services;

public interface IHoldService
{
    Task<HoldResponse> CreateAsync(CreateHoldRequest request, CancellationToken cancellationToken);

    Task<HoldResponse> GetAsync(string holdId, CancellationToken cancellationToken);

    Task<IReadOnlyList<HoldResponse>> ListAsync(HoldStatus? status, CancellationToken cancellationToken);

    Task<HoldResponse> ReleaseAsync(string holdId, CancellationToken cancellationToken);

    /// <summary>Closes every hold past its expiry. Returns how many this call expired.</summary>
    Task<int> ExpireDueAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<InventoryItemResponse>> GetInventoryAsync(CancellationToken cancellationToken);
}
