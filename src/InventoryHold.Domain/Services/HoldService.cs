using InventoryHold.Contracts.Enums;
using InventoryHold.Contracts.Events;
using InventoryHold.Contracts.Requests;
using InventoryHold.Contracts.Responses;
using InventoryHold.Domain.Abstractions;
using InventoryHold.Domain.Entities;
using InventoryHold.Domain.Exceptions;
using InventoryHold.Domain.Repositories;
using InventoryHold.Domain.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InventoryHold.Domain.Services;

/// <summary>
/// Hold lifecycle rules. MongoDB is the source of truth: Redis and RabbitMQ failures are logged and
/// never fail a request. Once stock has moved, work continues with CancellationToken.None so a client
/// disconnect cannot leave stock reserved with no hold to account for it.
/// </summary>
public sealed class HoldService(
    IHoldRepository holds,
    IProductRepository products,
    IInventoryCache cache,
    IEventPublisher publisher,
    TimeProvider clock,
    IOptions<HoldOptions> options,
    ILogger<HoldService> logger) : IHoldService
{
    private static readonly TimeSpan MaxHoldCacheTtl = TimeSpan.FromSeconds(60);

    private readonly HoldOptions _options = options.Value;

    public async Task<HoldResponse> CreateAsync(CreateHoldRequest request, CancellationToken cancellationToken)
    {
        CreateHoldRequestValidator.EnsureValid(request);

        // Reserving in a fixed order keeps competing multi-item holds from repeatedly undoing each other.
        var items = request.Items.OrderBy(i => i.Sku, StringComparer.Ordinal).ToList();
        var lines = await BuildLinesAsync(items, cancellationToken);

        await ReserveAllAsync(lines);

        var hold = Hold.Create(request.CustomerId, lines, clock.GetUtcNow(), _options.DefaultDuration);
        try
        {
            await holds.InsertAsync(hold, CancellationToken.None);
        }
        catch
        {
            await RestoreLinesAsync(hold.Id, lines);
            throw;
        }

        await InvalidateAsync(hold.Id);
        await PublishAsync(HoldEventTypes.Created, hold);
        return ToResponse(hold);
    }

    public async Task<HoldResponse> GetAsync(string holdId, CancellationToken cancellationToken)
    {
        var cached = await ReadCacheAsync(ct => cache.GetHoldAsync(holdId, ct), cancellationToken);
        if (cached is not null && !(cached.Status == HoldStatus.Active && cached.ExpiresAt <= clock.GetUtcNow()))
            return cached;

        var hold = await holds.GetByIdAsync(holdId, cancellationToken) ?? throw new HoldNotFoundException(holdId);
        if (hold.IsDue(clock.GetUtcNow()))
            hold = await ExpireAsync(hold);

        var response = ToResponse(hold);
        await TryCacheAsync(ct => cache.SetHoldAsync(response, CacheTtlFor(hold), ct), cancellationToken);
        return response;
    }

    public async Task<IReadOnlyList<HoldResponse>> ListAsync(HoldStatus? status, CancellationToken cancellationToken)
    {
        var found = await holds.ListAsync(status, _options.ListLimit, cancellationToken);
        var now = clock.GetUtcNow();

        var results = new List<HoldResponse>(found.Count);
        foreach (var hold in found)
        {
            var current = hold.IsDue(now) ? await ExpireAsync(hold) : hold;
            if (status is null || current.Status == status)
                results.Add(ToResponse(current));
        }
        return results;
    }

    public async Task<HoldResponse> ReleaseAsync(string holdId, CancellationToken cancellationToken)
    {
        var hold = await holds.GetByIdAsync(holdId, cancellationToken) ?? throw new HoldNotFoundException(holdId);
        if (hold.IsDue(clock.GetUtcNow()))
            hold = await ExpireAsync(hold);

        if (hold.Status != HoldStatus.Active)
            throw ClosedHoldException(hold);

        var released = await holds.TryCloseAsync(holdId, HoldStatus.Released, clock.GetUtcNow(), CancellationToken.None);
        if (released is null)
        {
            // Lost the race to a concurrent release or expiry, which has already restored the stock.
            var winner = await holds.GetByIdAsync(holdId, CancellationToken.None) ?? throw new HoldNotFoundException(holdId);
            throw ClosedHoldException(winner);
        }

        await RestoreLinesAsync(holdId, released.Lines);
        await InvalidateAsync(holdId);
        await PublishAsync(HoldEventTypes.Released, released);
        return ToResponse(released);
    }

    public async Task<int> ExpireDueAsync(CancellationToken cancellationToken)
    {
        var due = await holds.GetDueAsync(clock.GetUtcNow(), _options.SweepBatchSize, cancellationToken);
        var expired = 0;
        foreach (var hold in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await ExpireAsync(hold);
            if (result.Status == HoldStatus.Expired)
                expired++;
        }
        return expired;
    }

    public async Task<IReadOnlyList<InventoryItemResponse>> GetInventoryAsync(CancellationToken cancellationToken)
    {
        var cached = await ReadCacheAsync(cache.GetInventoryAsync, cancellationToken);
        if (cached is not null)
            return cached;

        var all = await products.GetAllAsync(cancellationToken);
        var inventory = all.Select(p => new InventoryItemResponse(p.Sku, p.Name, p.Available, p.Held)).ToList();

        await TryCacheAsync(ct => cache.SetInventoryAsync(inventory, ct), cancellationToken);
        return inventory;
    }

    private async Task<List<HoldLine>> BuildLinesAsync(List<HoldItemRequest> items, CancellationToken cancellationToken)
    {
        var skus = items.Select(i => i.Sku).ToList();
        var found = await products.GetBySkusAsync(skus, cancellationToken);
        var names = found.ToDictionary(p => p.Sku, p => p.Name);

        var unknown = skus.Where(s => !names.ContainsKey(s)).ToList();
        if (unknown.Count > 0)
            throw new ProductNotFoundException(unknown);

        return items.Select(i => new HoldLine(i.Sku, names[i.Sku], i.Quantity)).ToList();
    }

    private async Task ReserveAllAsync(List<HoldLine> lines)
    {
        var reserved = new List<HoldLine>(lines.Count);
        try
        {
            foreach (var line in lines)
            {
                if (!await products.TryReserveAsync(line.Sku, line.Quantity, CancellationToken.None))
                    throw new InsufficientStockException(line.Sku, line.Quantity);
                reserved.Add(line);
            }
        }
        catch
        {
            await RestoreLinesAsync(null, reserved);
            throw;
        }
    }

    /// <summary>Closes a due hold, returning the hold's final state whether this call or another closed it.</summary>
    private async Task<Hold> ExpireAsync(Hold hold)
    {
        var expired = await holds.TryCloseAsync(hold.Id, HoldStatus.Expired, clock.GetUtcNow(), CancellationToken.None);
        if (expired is null)
            return await holds.GetByIdAsync(hold.Id, CancellationToken.None) ?? throw new HoldNotFoundException(hold.Id);

        await RestoreLinesAsync(hold.Id, expired.Lines);
        await InvalidateAsync(hold.Id);
        await PublishAsync(HoldEventTypes.Expired, expired);
        return expired;
    }

    /// <summary>Attempts every line even if one fails, so one bad write does not strand the others.</summary>
    private async Task RestoreLinesAsync(string? holdId, IEnumerable<HoldLine> lines)
    {
        foreach (var line in lines)
        {
            try
            {
                await products.RestoreAsync(line.Sku, line.Quantity, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "Stock not restored and needs reconciliation: hold {HoldId}, SKU {Sku}, quantity {Quantity}",
                    holdId, line.Sku, line.Quantity);
            }
        }
    }

    private async Task InvalidateAsync(string holdId)
    {
        await TryCacheAsync(cache.InvalidateInventoryAsync, CancellationToken.None);
        await TryCacheAsync(ct => cache.InvalidateHoldAsync(holdId, ct), CancellationToken.None);
    }

    private async Task PublishAsync(string eventType, Hold hold)
    {
        var holdEvent = new HoldEvent(
            Guid.NewGuid(), eventType, clock.GetUtcNow(), hold.Id, hold.CustomerId, ToItems(hold), hold.ExpiresAt);
        try
        {
            await publisher.PublishAsync(holdEvent, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to publish {EventType} for hold {HoldId}", eventType, hold.Id);
        }
    }

    private async Task TryCacheAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        try
        {
            await operation(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Cache operation failed; continuing without the cache");
        }
    }

    private async Task<T?> ReadCacheAsync<T>(Func<CancellationToken, Task<T?>> operation, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await operation(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Cache read failed; falling back to the database");
            return null;
        }
    }

    private TimeSpan CacheTtlFor(Hold hold)
    {
        if (hold.Status != HoldStatus.Active)
            return MaxHoldCacheTtl;

        var untilExpiry = hold.ExpiresAt - clock.GetUtcNow();
        return untilExpiry < MaxHoldCacheTtl ? untilExpiry : MaxHoldCacheTtl;
    }

    private static DomainException ClosedHoldException(Hold hold) => hold.Status == HoldStatus.Released
        ? new HoldAlreadyReleasedException(hold.Id)
        : new HoldExpiredException(hold.Id);

    private static List<HoldItemResponse> ToItems(Hold hold) =>
        hold.Lines.Select(l => new HoldItemResponse(l.Sku, l.ProductName, l.Quantity)).ToList();

    private static HoldResponse ToResponse(Hold hold) =>
        new(hold.Id, hold.CustomerId, hold.Status, ToItems(hold), hold.CreatedAt, hold.ExpiresAt, hold.ClosedAt);
}
