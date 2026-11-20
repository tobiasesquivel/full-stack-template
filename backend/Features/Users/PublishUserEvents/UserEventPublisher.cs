using TemplateApp.Features.Users.Domain.Events;
using TemplateApp.Infrastructure.Messaging;

namespace TemplateApp.Features.Users.PublishUserEvents;

internal sealed class UserEventPublisher : IEventHandler<UserDeleted>, IEventHandler<AccountClosureRequested>
{
    private readonly IntegrationEventPublisher _broker;

    public UserEventPublisher(IntegrationEventPublisher broker)
    {
        _broker = broker;
    }

    public Task HandleAsync(UserDeleted @event, EventContext context, CancellationToken ct) =>
        _broker.PublishAsync(new UserDeletedIntegrationEvent(@event.AuthId, @event.DeletedAt), context, ct);

    public Task HandleAsync(AccountClosureRequested @event, EventContext context, CancellationToken ct) =>
        _broker.PublishAsync(new AccountClosureRequestedIntegrationEvent(@event.ClosureId, @event.AuthId, @event.RequestedAt), context, ct);
}
