namespace InventoryHold.Domain.Security;

public sealed class HmacOptions
{
    public const string SectionName = "Security:Hmac";
    public const int MinimumSecretLength = 32;

    /// <summary>On unless turned off explicitly, so a deployment that forgets this setting stays protected.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Client ID to shared secret. A blank secret disables that client.</summary>
    public Dictionary<string, string> Clients { get; set; } = [];

    public int TimestampToleranceSeconds { get; set; } = 300;

    public int MaxBodyBytes { get; set; } = 64 * 1024;

    public bool HasUsableClients =>
        Clients.Values.Any(s => !string.IsNullOrEmpty(s)) &&
        Clients.Values.All(s => string.IsNullOrEmpty(s) || s.Length >= MinimumSecretLength);
}
