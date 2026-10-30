using InventoryHold.Domain.Abstractions;
using InventoryHold.Domain.Repositories;
using InventoryHold.Infrastructure.Mongo;
using InventoryHold.Infrastructure.Options;
using InventoryHold.Infrastructure.RabbitMq;
using InventoryHold.Infrastructure.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using StackExchange.Redis;

namespace InventoryHold.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MongoOptions>().Bind(configuration.GetSection(MongoOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<RedisOptions>().Bind(configuration.GetSection(RedisOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<RabbitMqOptions>().Bind(configuration.GetSection(RabbitMqOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();

        services.AddSingleton<IMongoClient>(sp => new MongoClient(sp.GetRequiredService<IOptions<MongoOptions>>().Value.ConnectionString));
        services.AddSingleton<IMongoDatabase>(sp =>
            sp.GetRequiredService<IMongoClient>().GetDatabase(sp.GetRequiredService<IOptions<MongoOptions>>().Value.DatabaseName));
        services.AddSingleton<IProductRepository, MongoProductRepository>();
        services.AddSingleton<IHoldRepository, MongoHoldRepository>();

        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var redis = ConfigurationOptions.Parse(sp.GetRequiredService<IOptions<RedisOptions>>().Value.ConnectionString);
            redis.AbortOnConnectFail = false; // Keep retrying in the background rather than failing startup.
            redis.AsyncTimeout = 1000;        // A slow cache must not be slower than the database behind it.
            redis.BacklogPolicy = BacklogPolicy.FailFast; // The default queues commands during an outage, stalling each request for seconds.
            return ConnectionMultiplexer.Connect(redis);
        });
        services.AddSingleton<IInventoryCache, RedisInventoryCache>();

        services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();

        // Hosted services start in registration order, so this runs before the expiry sweeper and Kestrel.
        services.AddHostedService<MongoInitializer>();
        return services;
    }
}
