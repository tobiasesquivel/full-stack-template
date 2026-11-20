namespace TemplateApp.Infrastructure.Messaging;

public interface IIntegrationEventHandler<in TEvent>
    where TEvent : class
{
    Task HandleAsync(TEvent @event, EventContext context, CancellationToken ct);
}
