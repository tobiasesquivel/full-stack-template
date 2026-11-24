using System.Globalization;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Runtime.Handlers;

namespace TemplateApp.Infrastructure.Messaging.Bus;

internal sealed class IntegrationEventHandler<TEvent> : MessageHandler<TEvent>
    where TEvent : class
{
    private readonly IntegrationEventSubscription _subscription;

    public IntegrationEventHandler(IntegrationEventSubscription subscription)
    {
        _subscription = subscription;
    }

    protected override async Task HandleAsync(TEvent message, MessageContext context, CancellationToken cancellation)
    {
        Envelope envelope = context.Envelope!;
        int version = Header(envelope, MessageHeaders.Version, text => int.Parse(text, CultureInfo.InvariantCulture));
        if (version != _subscription.Event.Version)
            throw new UnreadableMessageException($"{_subscription.Event.Name} v{version} is unknown here: this consumer reads v{_subscription.Event.Version}.");

        Guid messageId = Header(envelope, MessageHeaders.MessageId, text => Guid.Parse(text, CultureInfo.InvariantCulture));
        DateTimeOffset occurredAt = Header(envelope, MessageHeaders.OccurredAt, text => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture));

        await using AsyncServiceScope scope = context.Runtime.Services.CreateAsyncScope();
        DateTimeOffset deliveredAt = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        await scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<TEvent>>()
            .HandleAsync(message, new EventContext(messageId, _subscription.Queue, occurredAt, deliveredAt), cancellation);
    }

    private T Header<T>(Envelope envelope, string name, Func<string, T> parse)
    {
        if (!envelope.Headers.TryGetValue(name, out string? text) || string.IsNullOrWhiteSpace(text))
            throw new UnreadableMessageException($"{_subscription.Event.Name} arrived without the {name} header.");

        try
        {
            return parse(text);
        }
        catch (Exception error) when (error is FormatException or OverflowException)
        {
            throw new UnreadableMessageException($"{_subscription.Event.Name} has an unreadable {name} header.", error);
        }
    }
}
