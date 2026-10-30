namespace InventoryHold.Domain.Services;

public sealed class HoldOptions
{
    public const string SectionName = "Holds";

    /// <summary>How long a hold lasts. Fractional values are allowed, which helps when demonstrating expiry.</summary>
    public double DefaultDurationMinutes { get; set; } = 15;

    public int SweepIntervalSeconds { get; set; } = 30;

    public int SweepBatchSize { get; set; } = 200;

    public int ListLimit { get; set; } = 100;

    public TimeSpan DefaultDuration => TimeSpan.FromMinutes(DefaultDurationMinutes);
}
