using TemplateApp.Shared.Domain;

namespace TemplateApp.Infrastructure.Messaging;

public interface IEventHandler<in TEvent>
    where TEvent : class, IDomainEvent
{
    Task HandleAsync(TEvent @event, EventContext context, CancellationToken ct);
}
