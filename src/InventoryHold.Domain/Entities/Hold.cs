using InventoryHold.Contracts.Enums;

namespace InventoryHold.Domain.Entities;

public sealed record HoldLine(string Sku, string ProductName, int Quantity);

/// <summary>
/// A hold moves one way only: Active -> Released or Active -> Expired.
/// The transition itself is applied by IHoldRepository.TryCloseAsync so it is atomic in the database.
/// </summary>
public sealed record Hold(
    string Id,
    string CustomerId,
    HoldStatus Status,
    IReadOnlyList<HoldLine> Lines,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ClosedAt)
{
    public static Hold Create(string customerId, IReadOnlyList<HoldLine> lines, DateTimeOffset now, TimeSpan duration) =>
        new(Guid.NewGuid().ToString(), customerId, HoldStatus.Active, lines, now, now + duration, null);

    public bool IsDue(DateTimeOffset now) => Status == HoldStatus.Active && ExpiresAt <= now;
}
