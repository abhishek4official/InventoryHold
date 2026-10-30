using InventoryHold.Contracts.Events;

namespace InventoryHold.Domain.Abstractions;

public interface IEventPublisher
{
    Task PublishAsync(HoldEvent holdEvent, CancellationToken cancellationToken);
}
