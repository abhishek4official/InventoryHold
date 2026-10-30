using InventoryHold.Contracts.Enums;
using InventoryHold.Contracts.Events;
using InventoryHold.Domain.Entities;
using InventoryHold.Domain.Exceptions;
using InventoryHold.Domain.Services;
using Moq;

namespace InventoryHold.UnitTests;

[TestFixture]
public class ItemTrackingTests : HoldServiceTestBase
{
    private void GivenCatalog(params Product[] catalog) =>
        Products.Setup(p => p.GetBySkusAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalog);

    private static Product Serialized(string sku) =>
        new(sku, $"Name of {sku}", 10, 0, PartNumber: $"PN-{sku}", HandlingDays: 2, IsSerialized: true);

    private void GivenClaim(string sku, params string[] serials) =>
        Units.Setup(u => u.ClaimAsync(sku, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(serials);

    [Test]
    public async Task Serialized_line_gets_its_serial_numbers_part_number_and_ship_date()
    {
        GivenCatalog(Serialized("SKU-A"));
        GivenClaim("SKU-A", "S1", "S2");

        var hold = await Service.CreateAsync(Request(("SKU-A", 2)), CancellationToken.None);

        // The test clock is Thursday 2026-01-01 at 12:00 UTC, before the 14:00 cutoff.
        var item = hold.Items.Single();
        Assert.Multiple(() =>
        {
            Assert.That(item.SerialNumbers, Is.EqualTo(new[] { "S1", "S2" }));
            Assert.That(item.PartNumber, Is.EqualTo("PN-SKU-A"));
            Assert.That(item.ApproxShipDate, Is.EqualTo(new DateOnly(2026, 1, 5)));
        });
        Units.Verify(u => u.ClaimAsync("SKU-A", hold.Id, 2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Line_not_tracked_by_serial_claims_no_units()
    {
        var hold = await Service.CreateAsync(Request(("SKU-A", 2)), CancellationToken.None);

        Assert.That(hold.Items.Single().SerialNumbers, Is.Empty);
        Units.Verify(u => u.ClaimAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void Too_few_free_units_fails_the_hold_and_returns_stock_and_units()
    {
        GivenCatalog(Serialized("SKU-A"));
        GivenClaim("SKU-A", "S1");

        Assert.ThrowsAsync<InsufficientStockException>(() => Service.CreateAsync(Request(("SKU-A", 2)), CancellationToken.None));

        Products.Verify(p => p.RestoreAsync("SKU-A", 2, It.IsAny<CancellationToken>()), Times.Once);
        Units.Verify(u => u.ReleaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        Holds.Verify(h => h.InsertAsync(It.IsAny<Hold>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Releasing_a_hold_frees_its_units_before_restoring_the_count()
    {
        var hold = ActiveHold();
        GivenStored(hold);
        GivenCloseSucceeds(hold);
        var order = new List<string>();
        Units.Setup(u => u.ReleaseAsync(hold.Id, It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("units")).Returns(Task.CompletedTask);
        Products.Setup(p => p.RestoreAsync("SKU-A", 2, It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("count")).Returns(Task.CompletedTask);

        await Service.ReleaseAsync(hold.Id, CancellationToken.None);

        Assert.That(order, Is.EqualTo(new[] { "units", "count" }));
    }

    [Test]
    public async Task Expiring_a_hold_frees_its_units()
    {
        var hold = ActiveHold();
        GivenStored(hold);
        GivenCloseSucceeds(hold);
        Clock.Advance(Duration);

        await Service.GetAsync(hold.Id, CancellationToken.None);

        Units.Verify(u => u.ReleaseAsync(hold.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public void Failing_to_free_units_still_restores_the_count()
    {
        var hold = ActiveHold();
        GivenStored(hold);
        GivenCloseSucceeds(hold);
        Units.Setup(u => u.ReleaseAsync(hold.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("write failed"));

        Assert.DoesNotThrowAsync(() => Service.ReleaseAsync(hold.Id, CancellationToken.None));

        Products.Verify(p => p.RestoreAsync("SKU-A", 2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Events_carry_part_numbers_and_serial_numbers()
    {
        GivenCatalog(Serialized("SKU-A"));
        GivenClaim("SKU-A", "S1");
        HoldEvent? published = null;
        Publisher.Setup(p => p.PublishAsync(It.IsAny<HoldEvent>(), It.IsAny<CancellationToken>()))
            .Callback((HoldEvent e, CancellationToken _) => published = e).Returns(Task.CompletedTask);

        await Service.CreateAsync(Request(("SKU-A", 1)), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(published!.Items.Single().PartNumber, Is.EqualTo("PN-SKU-A"));
            Assert.That(published.Items.Single().SerialNumbers, Is.EqualTo(new[] { "S1" }));
        });
    }

    [Test]
    public async Task Inventory_flags_low_stock_and_has_no_ship_date_when_sold_out()
    {
        Products.Setup(p => p.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new Product("SOLD-OUT", "A", 0, 3, ReorderPoint: 2),
            new Product("LOW", "B", 2, 0, ReorderPoint: 2),
            new Product("FINE", "C", 3, 0, ReorderPoint: 2)
        ]);

        var result = (await Service.GetInventoryAsync(CancellationToken.None)).ToDictionary(i => i.Sku);

        Assert.Multiple(() =>
        {
            Assert.That(result["SOLD-OUT"].LowStock, Is.True);
            Assert.That(result["SOLD-OUT"].ApproxShipDate, Is.Null);
            Assert.That(result["LOW"].LowStock, Is.True);
            Assert.That(result["LOW"].ApproxShipDate, Is.Not.Null);
            Assert.That(result["FINE"].LowStock, Is.False);
        });
    }

    // 2026-01-01 is a Thursday. Expected values are the ship date as yyyy-MM-dd.
    [TestCase("2026-01-01T12:00:00Z", 1, "2026-01-02", TestName = "Before cutoff: next business day")]
    [TestCase("2026-01-01T14:00:00Z", 1, "2026-01-05", TestName = "At cutoff: counts from Friday, ships Monday")]
    [TestCase("2026-01-02T12:00:00Z", 1, "2026-01-05", TestName = "Friday order skips the weekend")]
    [TestCase("2026-01-03T12:00:00Z", 1, "2026-01-06", TestName = "Saturday order counts from Monday")]
    [TestCase("2026-01-01T12:00:00Z", 5, "2026-01-08", TestName = "Five handling days span a weekend")]
    [TestCase("2026-01-01T12:00:00Z", 0, "2026-01-01", TestName = "Zero handling days ships the same day")]
    public void Ship_date_estimate(string now, int handlingDays, string expected) =>
        Assert.That(ShipDateEstimator.Estimate(DateTimeOffset.Parse(now), handlingDays, cutoffHourUtc: 14),
            Is.EqualTo(DateOnly.Parse(expected)));
}
