using TemplateApp.Shared.Domain;

namespace TemplateApp.Infrastructure.Messaging.Outbox;

public interface IEventHandler<in TEvent>
    where TEvent : class, IDomainEvent
{
    Task HandleAsync(TEvent @event, EventContext context, CancellationToken ct);
}
