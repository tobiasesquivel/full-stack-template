namespace TemplateApp.Infrastructure.Messaging.Bus;

public interface IIntegrationEventHandler<in TEvent>
    where TEvent : class
{
    Task HandleAsync(TEvent @event, EventContext context, CancellationToken ct);
}
