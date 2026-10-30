using InventoryHold.Domain.Services;
using Microsoft.Extensions.Options;

namespace InventoryHold.WebApi.BackgroundServices;

/// <summary>
/// Returns stock for holds nobody asks about again. Safe to run on several instances:
/// the compare-and-set in the repository lets only one of them close any given hold.
/// </summary>
public sealed class HoldExpirySweeper(
    IServiceScopeFactory scopes,
    IOptions<HoldOptions> options,
    ILogger<HoldExpirySweeper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.SweepIntervalSeconds));
        while (await WaitForNextTickAsync(timer, stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var expired = await scope.ServiceProvider.GetRequiredService<IHoldService>().ExpireDueAsync(stoppingToken);
                if (expired > 0)
                    logger.LogInformation("Expired {Count} hold(s)", expired);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // A database blip must not kill the sweeper; the next tick tries again.
                logger.LogError(ex, "Hold expiry sweep failed");
            }
        }
    }

    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
