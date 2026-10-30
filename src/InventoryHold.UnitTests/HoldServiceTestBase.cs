using InventoryHold.Contracts.Enums;
using InventoryHold.Contracts.Requests;
using InventoryHold.Domain.Abstractions;
using InventoryHold.Domain.Entities;
using InventoryHold.Domain.Repositories;
using InventoryHold.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace InventoryHold.UnitTests;

public abstract class HoldServiceTestBase
{
    protected static readonly TimeSpan Duration = TimeSpan.FromMinutes(15);

    protected Mock<IHoldRepository> Holds = null!;
    protected Mock<IProductRepository> Products = null!;
    protected Mock<IInventoryUnitRepository> Units = null!;
    protected Mock<IInventoryCache> Cache = null!;
    protected Mock<IEventPublisher> Publisher = null!;
    protected FakeTimeProvider Clock = null!;
    protected HoldService Service = null!;

    [SetUp]
    public void SetUpMocks()
    {
        Holds = new Mock<IHoldRepository>();
        Products = new Mock<IProductRepository>();
        Units = new Mock<IInventoryUnitRepository>();
        Cache = new Mock<IInventoryCache>();
        Publisher = new Mock<IEventPublisher>();
        Clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

        Products
            .Setup(p => p.GetBySkusAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<string> skus, CancellationToken _) =>
                skus.Where(s => s.StartsWith("SKU")).Select(s => new Product(s, $"Name of {s}", 100, 0)).ToList());
        Products
            .Setup(p => p.TryReserveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        Service = new HoldService(
            Holds.Object,
            Products.Object,
            Units.Object,
            Cache.Object,
            Publisher.Object,
            Clock,
            Options.Create(new HoldOptions { DefaultDurationMinutes = Duration.TotalMinutes }),
            NullLogger<HoldService>.Instance);
    }

    protected static CreateHoldRequest Request(params (string Sku, int Quantity)[] items) =>
        new("customer-1", items.Select(i => new HoldItemRequest(i.Sku, i.Quantity)).ToList());

    protected Hold ActiveHold(string id = "hold-1", DateTimeOffset? expiresAt = null) =>
        new(id, "customer-1", HoldStatus.Active, [new HoldLine("SKU-A", "Name of SKU-A", 2)],
            Clock.GetUtcNow(), expiresAt ?? Clock.GetUtcNow() + Duration, null);

    protected void GivenStored(Hold hold) =>
        Holds.Setup(h => h.GetByIdAsync(hold.Id, It.IsAny<CancellationToken>())).ReturnsAsync(hold);

    /// <summary>Behaves as the real compare-and-set does: closes an Active hold, returns null otherwise.</summary>
    protected void GivenCloseSucceeds(Hold hold) =>
        Holds.Setup(h => h.TryCloseAsync(hold.Id, It.IsAny<HoldStatus>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, HoldStatus status, DateTimeOffset at, CancellationToken _) =>
                hold with { Status = status, ClosedAt = at });
}
