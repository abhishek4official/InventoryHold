namespace InventoryHold.Domain.Services;

/// <summary>
/// Approximate dispatch date: orders after the daily cutoff count from the next business day, then
/// handling days are added, skipping weekends. Public holidays and carrier transit are not modelled.
/// </summary>
public static class ShipDateEstimator
{
    public static DateOnly Estimate(DateTimeOffset now, int handlingDays, int cutoffHourUtc)
    {
        var utc = now.UtcDateTime;
        var date = DateOnly.FromDateTime(utc);
        if (utc.Hour >= cutoffHourUtc)
            date = date.AddDays(1);

        while (!IsBusinessDay(date))
            date = date.AddDays(1);

        for (var remaining = handlingDays; remaining > 0; remaining--)
        {
            date = date.AddDays(1);
            while (!IsBusinessDay(date))
                date = date.AddDays(1);
        }
        return date;
    }

    private static bool IsBusinessDay(DateOnly date) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
}
