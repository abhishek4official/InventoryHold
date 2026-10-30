using InventoryHold.Contracts.Enums;
using InventoryHold.Contracts.Events;
using InventoryHold.Contracts.Responses;
using InventoryHold.Domain.Entities;
using InventoryHold.Domain.Exceptions;
using Moq;

namespace InventoryHold.UnitTests;

[TestFixture]
public class ConcurrencyAndResilienceTests : HoldServiceTestBase
{
    [Test]
    public async Task Parallel_creates_never_oversell()
    {
        // Stands in for the conditional $inc: the check and the decrement happen as one step.
        var available = 10;
        Products.Setup(p => p.TryReserveAsync("SKU-A", 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                while (true)
                {
                    var current = Volatile.Read(ref available);
                    if (current < 1)
                        return false;
                    if (Interlocked.CompareExchange(ref available, current - 1, current) == current)
                        return true;
                }
            });

        var attempts = Enumerable.Range(0, 50).Select(_ => Task.Run(async () =>
        {
            try
            {
                await Service.CreateAsync(Request(("SKU-A", 1)), CancellationToken.None);
                return true;
            }
            catch (InsufficientStockException)
            {
                return false;
            }
        }));
        var results = await Task.WhenAll(attempts);

        Assert.Multiple(() =>
        {
            Assert.That(results.Count(won => won), Is.EqualTo(10));
            Assert.That(available, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task Racing_releases_restock_exactly_once()
    {
        var hold = ActiveHold();
        var closed = 0;
        Holds.Setup(h => h.TryCloseAsync(hold.Id, It.IsAny<HoldStatus>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, HoldStatus status, DateTimeOffset at, CancellationToken _) =>
                Interlocked.Exchange(ref closed, 1) == 0 ? hold with { Status = status, ClosedAt = at } : null);
        Holds.Setup(h => h.GetByIdAsync(hold.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Volatile.Read(ref closed) == 1 ? hold with { Status = HoldStatus.Released } : hold);

        var attempts = Enumerable.Range(0, 20).Select(_ => Task.Run(async () =>
        {
            try
            {
                await Service.ReleaseAsync(hold.Id, CancellationToken.None);
                return true;
            }
            catch (HoldAlreadyReleasedException)
            {
                return false;
            }
        }));
        var results = await Task.WhenAll(attempts);

        Assert.That(results.Count(won => won), Is.EqualTo(1));
        Products.Verify(p => p.RestoreAsync("SKU-A", 2, It.IsAny<CancellationToken>()), Times.Once);
        Publisher.Verify(p => p.PublishAsync(It.IsAny<HoldEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public void Release_losing_the_race_to_the_sweeper_reports_expired_and_does_not_restock()
    {
        var hold = ActiveHold();
        Holds.SetupSequence(h => h.GetByIdAsync(hold.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(hold)
            .ReturnsAsync(hold with { Status = HoldStatus.Expired });
        Holds.Setup(h => h.TryCloseAsync(hold.Id, HoldStatus.Released, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Hold?)null);

        Assert.ThrowsAsync<HoldExpiredException>(() => Service.ReleaseAsync(hold.Id, CancellationToken.None));

        Products.Verify(p => p.RestoreAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Sweep_does_not_count_or_restock_a_hold_released_in_the_meantime()
    {
        var hold = ActiveHold(expiresAt: Clock.GetUtcNow().AddMinutes(-1));
        Holds.Setup(h => h.GetDueAsync(It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([hold]);
        Holds.Setup(h => h.TryCloseAsync(hold.Id, HoldStatus.Expired, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Hold?)null);
        GivenStored(hold with { Status = HoldStatus.Released });

        var expired = await Service.ExpireDueAsync(CancellationToken.None);

        Assert.That(expired, Is.EqualTo(0));
        Products.Verify(p => p.RestoreAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void Create_succeeds_when_the_broker_is_down()
    {
        Publisher.Setup(p => p.PublishAsync(It.IsAny<HoldEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("broker unreachable"));

        Assert.DoesNotThrowAsync(() => Service.CreateAsync(Request(("SKU-A", 1)), CancellationToken.None));
    }

    [Test]
    public void Release_succeeds_when_the_cache_is_down()
    {
        var hold = ActiveHold();
        GivenStored(hold);
        GivenCloseSucceeds(hold);
        Cache.Setup(c => c.InvalidateInventoryAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("cache unreachable"));

        Assert.DoesNotThrowAsync(() => Service.ReleaseAsync(hold.Id, CancellationToken.None));
    }

    [Test]
    public async Task Reads_fall_back_to_the_database_when_the_cache_is_down()
    {
        Cache.Setup(c => c.GetInventoryAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("cache unreachable"));
        Products.Setup(p => p.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([new Product("SKU-A", "A", 5, 0)]);

        var result = await Service.GetInventoryAsync(CancellationToken.None);

        Assert.That((result.Single().Sku, result.Single().Available), Is.EqualTo(("SKU-A", 5)));
    }

    [Test]
    public async Task One_failed_restore_does_not_strand_the_other_lines()
    {
        var hold = ActiveHold() with
        {
            Lines = [new HoldLine("SKU-A", "A", 1), new HoldLine("SKU-B", "B", 2)]
        };
        GivenStored(hold);
        GivenCloseSucceeds(hold);
        Products.Setup(p => p.RestoreAsync("SKU-A", 1, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("write failed"));

        await Service.ReleaseAsync(hold.Id, CancellationToken.None);

        Products.Verify(p => p.RestoreAsync("SKU-B", 2, It.IsAny<CancellationToken>()), Times.Once);
    }
}
