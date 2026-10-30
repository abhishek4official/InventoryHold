using InventoryHold.Contracts.Enums;
using InventoryHold.Contracts.Events;
using InventoryHold.Contracts.Responses;
using InventoryHold.Domain.Entities;
using InventoryHold.Domain.Exceptions;
using Moq;

namespace InventoryHold.UnitTests;

[TestFixture]
public class HoldLifecycleTests : HoldServiceTestBase
{
    private void VerifyPublished(string eventType, Times times) =>
        Publisher.Verify(p => p.PublishAsync(It.Is<HoldEvent>(e => e.EventType == eventType), It.IsAny<CancellationToken>()), times);

    private void VerifyRestored(string sku, int quantity, Times times) =>
        Products.Verify(p => p.RestoreAsync(sku, quantity, It.IsAny<CancellationToken>()), times);

    [Test]
    public async Task Create_reserves_persists_invalidates_and_publishes()
    {
        var hold = await Service.CreateAsync(Request(("SKU-A", 2)), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(hold.Status, Is.EqualTo(HoldStatus.Active));
            Assert.That(hold.ExpiresAt, Is.EqualTo(Clock.GetUtcNow() + Duration));
            Assert.That(hold.Items.Single().ProductName, Is.EqualTo("Name of SKU-A"));
        });
        Products.Verify(p => p.TryReserveAsync("SKU-A", 2, It.IsAny<CancellationToken>()), Times.Once);
        Holds.Verify(h => h.InsertAsync(It.Is<Hold>(x => x.Id == hold.Id), It.IsAny<CancellationToken>()), Times.Once);
        Cache.Verify(c => c.InvalidateInventoryAsync(It.IsAny<CancellationToken>()), Times.Once);
        VerifyPublished(HoldEventTypes.Created, Times.Once());
    }

    [Test]
    public void Create_with_unknown_sku_reserves_nothing()
    {
        var ex = Assert.ThrowsAsync<ProductNotFoundException>(() =>
            Service.CreateAsync(Request(("SKU-A", 1), ("MISSING", 1)), CancellationToken.None));

        Assert.That(ex!.Skus, Is.EquivalentTo(new[] { "MISSING" }));
        Products.Verify(p => p.TryReserveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void Create_with_insufficient_stock_writes_no_hold_and_no_event()
    {
        Products.Setup(p => p.TryReserveAsync("SKU-A", 5, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        Assert.ThrowsAsync<InsufficientStockException>(() => Service.CreateAsync(Request(("SKU-A", 5)), CancellationToken.None));

        Holds.Verify(h => h.InsertAsync(It.IsAny<Hold>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyPublished(HoldEventTypes.Created, Times.Never());
    }

    [Test]
    public void Create_partial_failure_returns_only_the_lines_already_reserved()
    {
        Products.Setup(p => p.TryReserveAsync("SKU-B", 2, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        Assert.ThrowsAsync<InsufficientStockException>(() =>
            Service.CreateAsync(Request(("SKU-C", 3), ("SKU-A", 1), ("SKU-B", 2)), CancellationToken.None));

        // Reserved in sorted order, so A succeeded, B failed and C was never attempted.
        VerifyRestored("SKU-A", 1, Times.Once());
        VerifyRestored("SKU-B", 2, Times.Never());
        VerifyRestored("SKU-C", 3, Times.Never());
    }

    [Test]
    public void Create_restores_stock_if_saving_the_hold_fails()
    {
        Holds.Setup(h => h.InsertAsync(It.IsAny<Hold>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("write failed"));

        Assert.ThrowsAsync<InvalidOperationException>(() => Service.CreateAsync(Request(("SKU-A", 2)), CancellationToken.None));

        VerifyRestored("SKU-A", 2, Times.Once());
    }

    [Test]
    public void Create_keeps_going_with_stock_when_the_client_disconnects()
    {
        using var cancelled = new CancellationTokenSource();
        Products.Setup(p => p.TryReserveAsync("SKU-A", 1, It.IsAny<CancellationToken>()))
            .Callback(cancelled.Cancel).ReturnsAsync(true);

        Assert.DoesNotThrowAsync(() => Service.CreateAsync(Request(("SKU-A", 1)), cancelled.Token));

        Holds.Verify(h => h.InsertAsync(It.IsAny<Hold>(), CancellationToken.None), Times.Once);
    }

    [Test]
    public void Get_unknown_hold_throws_not_found() =>
        Assert.ThrowsAsync<HoldNotFoundException>(() => Service.GetAsync("nope", CancellationToken.None));

    [Test]
    public async Task Get_active_hold_returns_it_and_caches_it()
    {
        var hold = ActiveHold();
        GivenStored(hold);

        var result = await Service.GetAsync(hold.Id, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(HoldStatus.Active));
        Cache.Verify(c => c.SetHoldAsync(result, TimeSpan.FromSeconds(60), It.IsAny<CancellationToken>()), Times.Once);
        Holds.Verify(h => h.TryCloseAsync(It.IsAny<string>(), It.IsAny<HoldStatus>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Get_caps_cache_ttl_at_the_time_left_on_the_hold()
    {
        var hold = ActiveHold(expiresAt: Clock.GetUtcNow().AddSeconds(10));
        GivenStored(hold);

        await Service.GetAsync(hold.Id, CancellationToken.None);

        Cache.Verify(c => c.SetHoldAsync(It.IsAny<HoldResponse>(), TimeSpan.FromSeconds(10), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Get_due_hold_expires_it_restores_stock_and_publishes()
    {
        var hold = ActiveHold();
        GivenStored(hold);
        GivenCloseSucceeds(hold);
        Clock.Advance(Duration);

        var result = await Service.GetAsync(hold.Id, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(HoldStatus.Expired));
        VerifyRestored("SKU-A", 2, Times.Once());
        VerifyPublished(HoldEventTypes.Expired, Times.Once());
    }

    [Test]
    public async Task Get_serves_from_cache_without_touching_the_database()
    {
        var cached = new HoldResponse("hold-1", "c", HoldStatus.Active, [], Clock.GetUtcNow(), Clock.GetUtcNow() + Duration, null);
        Cache.Setup(c => c.GetHoldAsync("hold-1", It.IsAny<CancellationToken>())).ReturnsAsync(cached);

        var result = await Service.GetAsync("hold-1", CancellationToken.None);

        Assert.That(result, Is.SameAs(cached));
        Holds.Verify(h => h.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Get_does_not_trust_a_cached_active_hold_past_its_expiry()
    {
        var hold = ActiveHold();
        GivenStored(hold);
        GivenCloseSucceeds(hold);
        Cache.Setup(c => c.GetHoldAsync(hold.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HoldResponse(hold.Id, "c", HoldStatus.Active, [], hold.CreatedAt, hold.ExpiresAt, null));
        Clock.Advance(Duration);

        var result = await Service.GetAsync(hold.Id, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(HoldStatus.Expired));
    }

    [Test]
    public async Task Release_restores_stock_invalidates_and_publishes()
    {
        var hold = ActiveHold();
        GivenStored(hold);
        GivenCloseSucceeds(hold);

        var result = await Service.ReleaseAsync(hold.Id, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(HoldStatus.Released));
            Assert.That(result.ClosedAt, Is.EqualTo(Clock.GetUtcNow()));
        });
        VerifyRestored("SKU-A", 2, Times.Once());
        Cache.Verify(c => c.InvalidateInventoryAsync(It.IsAny<CancellationToken>()), Times.Once);
        Cache.Verify(c => c.InvalidateHoldAsync(hold.Id, It.IsAny<CancellationToken>()), Times.Once);
        VerifyPublished(HoldEventTypes.Released, Times.Once());
    }

    [Test]
    public void Release_unknown_hold_throws_not_found() =>
        Assert.ThrowsAsync<HoldNotFoundException>(() => Service.ReleaseAsync("nope", CancellationToken.None));

    [Test]
    public void Release_already_released_hold_is_rejected_without_restocking()
    {
        var hold = ActiveHold() with { Status = HoldStatus.Released };
        GivenStored(hold);

        Assert.ThrowsAsync<HoldAlreadyReleasedException>(() => Service.ReleaseAsync(hold.Id, CancellationToken.None));

        VerifyRestored("SKU-A", 2, Times.Never());
    }

    [Test]
    public void Release_already_expired_hold_is_rejected_without_restocking()
    {
        var hold = ActiveHold() with { Status = HoldStatus.Expired };
        GivenStored(hold);

        Assert.ThrowsAsync<HoldExpiredException>(() => Service.ReleaseAsync(hold.Id, CancellationToken.None));

        VerifyRestored("SKU-A", 2, Times.Never());
    }

    [Test]
    public void Release_of_a_due_hold_the_sweeper_missed_expires_it_rather_than_releasing()
    {
        var hold = ActiveHold();
        GivenStored(hold);
        GivenCloseSucceeds(hold);
        Clock.Advance(Duration);

        Assert.ThrowsAsync<HoldExpiredException>(() => Service.ReleaseAsync(hold.Id, CancellationToken.None));

        VerifyRestored("SKU-A", 2, Times.Once());
        VerifyPublished(HoldEventTypes.Expired, Times.Once());
        VerifyPublished(HoldEventTypes.Released, Times.Never());
    }

    [Test]
    public async Task List_active_expires_due_holds_and_leaves_them_out()
    {
        var live = ActiveHold("live", Clock.GetUtcNow().AddMinutes(5));
        var due = ActiveHold("due", Clock.GetUtcNow().AddMinutes(-1));
        Holds.Setup(h => h.ListAsync(HoldStatus.Active, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([live, due]);
        GivenCloseSucceeds(due);

        var result = await Service.ListAsync(HoldStatus.Active, CancellationToken.None);

        Assert.That(result.Select(h => h.Id), Is.EqualTo(new[] { "live" }));
        VerifyPublished(HoldEventTypes.Expired, Times.Once());
    }

    [Test]
    public async Task Sweep_expires_each_due_hold()
    {
        var first = ActiveHold("h1", Clock.GetUtcNow().AddMinutes(-1));
        var second = ActiveHold("h2", Clock.GetUtcNow().AddMinutes(-2));
        Holds.Setup(h => h.GetDueAsync(It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([first, second]);
        GivenCloseSucceeds(first);
        GivenCloseSucceeds(second);

        var expired = await Service.ExpireDueAsync(CancellationToken.None);

        Assert.That(expired, Is.EqualTo(2));
        VerifyPublished(HoldEventTypes.Expired, Times.Exactly(2));
    }

    [Test]
    public async Task Inventory_miss_loads_from_database_and_populates_cache()
    {
        Products.Setup(p => p.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([new Product("SKU-A", "A", 5, 1)]);

        var result = await Service.GetInventoryAsync(CancellationToken.None);

        Assert.That((result.Single().Sku, result.Single().Available, result.Single().Held), Is.EqualTo(("SKU-A", 5, 1)));
        Cache.Verify(c => c.SetInventoryAsync(It.IsAny<IReadOnlyList<InventoryItemResponse>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Inventory_hit_skips_the_database()
    {
        Cache.Setup(c => c.GetInventoryAsync(It.IsAny<CancellationToken>())).ReturnsAsync([new InventoryItemResponse("SKU-A", "A", "P", "M", "C", "L", "EA", 5, 1, 0, false, false, null, null, [])]);

        await Service.GetInventoryAsync(CancellationToken.None);

        Products.Verify(p => p.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
