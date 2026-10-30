using System.Text.Json;
using InventoryHold.Contracts.Events;
using InventoryHold.Domain.Abstractions;
using InventoryHold.Infrastructure.Options;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace InventoryHold.Infrastructure.RabbitMq;

/// <summary>
/// Publishes to a durable topic exchange with publisher confirms. Connects on first use, so the API
/// can start and serve stock while the broker is unavailable. A channel must not be shared by
/// concurrent publishers, so publishes are serialized.
/// </summary>
internal sealed class RabbitMqEventPublisher(IOptions<RabbitMqOptions> options) : IEventPublisher, IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly RabbitMqOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public async Task PublishAsync(HoldEvent holdEvent, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.PublishTimeoutSeconds));

        await _gate.WaitAsync(timeout.Token);
        try
        {
            var channel = await GetChannelAsync(timeout.Token);
            var properties = new BasicProperties
            {
                Persistent = true,
                ContentType = "application/json",
                MessageId = holdEvent.EventId.ToString(),
                Type = holdEvent.EventType,
                Timestamp = new AmqpTimestamp(holdEvent.OccurredAt.ToUnixTimeSeconds())
            };

            await channel.BasicPublishAsync(
                _options.Exchange,
                RoutingKeyFor(holdEvent.EventType),
                mandatory: false,
                properties,
                JsonSerializer.SerializeToUtf8Bytes(holdEvent, Json),
                timeout.Token);
        }
        finally
        {
            _gate.Release();
        }
    }

    public static string RoutingKeyFor(string eventType) => eventType switch
    {
        HoldEventTypes.Created => "hold.created",
        HoldEventTypes.Released => "hold.released",
        HoldEventTypes.Expired => "hold.expired",
        _ => throw new ArgumentOutOfRangeException(nameof(eventType), eventType, "Unknown event type")
    };

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
            return _channel;

        if (_connection is not { IsOpen: true })
        {
            var factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password,
                VirtualHost = _options.VirtualHost,
                ClientProvidedName = "inventory-hold-service"
            };
            _connection = await factory.CreateConnectionAsync(cancellationToken);
        }

        var channel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);

        await channel.ExchangeDeclareAsync(_options.Exchange, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(_options.Queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(_options.Queue, _options.Exchange, "hold.#", cancellationToken: cancellationToken);

        _channel = channel;
        return channel;
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
            await _channel.DisposeAsync();
        if (_connection is not null)
            await _connection.DisposeAsync();
        _gate.Dispose();
    }
}
