using System.Globalization;
using Wolverine;

namespace TemplateApp.Infrastructure.Messaging.Bus;

public sealed class IntegrationEventPublisher
{
    private readonly IMessageBus _bus;
    private readonly Dictionary<Type, IntegrationEventType> _types;

    public IntegrationEventPublisher(IMessageBus bus, IEnumerable<IntegrationEventType> types)
    {
        _bus = bus;
        _types = types.ToDictionary(type => type.ClrType);
    }

    public async Task PublishAsync<TEvent>(TEvent @event, EventContext context, CancellationToken ct)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(context);
        IntegrationEventType type = _types.TryGetValue(typeof(TEvent), out IntegrationEventType? found)
            ? found
            : throw new InvalidOperationException($"{typeof(TEvent).Name} has no stable name: register it with AddIntegrationEventPublication.");
        ct.ThrowIfCancellationRequested();

        DeliveryOptions options = new DeliveryOptions()
            .WithHeader(MessageHeaders.MessageId, context.MessageId.ToString("D"))
            .WithHeader(MessageHeaders.Version, type.Version.ToString(CultureInfo.InvariantCulture))
            .WithHeader(MessageHeaders.OccurredAt, context.OccurredAt.ToString("O", CultureInfo.InvariantCulture));
        try
        {
            await _bus.SendAsync(@event, options);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            throw new BrokerUnavailableException($"RabbitMQ did not confirm {type.Name} {context.MessageId}.", error);
        }
    }
}
