namespace InventoryHold.Domain.Services;

public sealed class HoldOptions
{
    public const string SectionName = "Holds";

    /// <summary>How long a hold lasts. Fractional values are allowed, which helps when demonstrating expiry.</summary>
    public double DefaultDurationMinutes { get; set; } = 15;

    public int SweepIntervalSeconds { get; set; } = 30;

    public int SweepBatchSize { get; set; } = 200;

    public int ListLimit { get; set; } = 100;

    /// <summary>Orders after this UTC hour count as placed the next business day when estimating ship dates.</summary>
    public int ShippingCutoffHourUtc { get; set; } = 14;

    public TimeSpan DefaultDuration => TimeSpan.FromMinutes(DefaultDurationMinutes);
}
