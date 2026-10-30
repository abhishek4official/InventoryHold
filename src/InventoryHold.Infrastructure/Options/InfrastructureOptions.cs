using System.ComponentModel.DataAnnotations;

namespace InventoryHold.Infrastructure.Options;

public sealed class MongoOptions
{
    public const string SectionName = "Mongo";

    [Required] public string ConnectionString { get; set; } = "";
    [Required] public string DatabaseName { get; set; } = "";
}

public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    [Required] public string ConnectionString { get; set; } = "";
    public string KeyPrefix { get; set; } = "inventory-hold:";
    public int InventoryTtlSeconds { get; set; } = 30;
}

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    [Required] public string HostName { get; set; } = "";
    public int Port { get; set; } = 5672;
    [Required] public string UserName { get; set; } = "";
    [Required] public string Password { get; set; } = "";
    public string VirtualHost { get; set; } = "/";
    public string Exchange { get; set; } = "inventory.holds";
    public string Queue { get; set; } = "inventory-hold.events";
    public int PublishTimeoutSeconds { get; set; } = 5;
}
