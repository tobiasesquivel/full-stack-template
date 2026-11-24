using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Auth.Domain.Events;
using TemplateApp.Infrastructure.Messaging;
using TemplateApp.Infrastructure.Messaging.Bus;
using TemplateApp.Infrastructure.Messaging.Outbox;

namespace TemplateApp.Features.Auth.PublishAuthEvents;

internal sealed class AuthEventPublisher : IEventHandler<AccountDeleted>, IEventHandler<AccountClosureRejected>
{
    private readonly IntegrationEventPublisher _broker;

    public AuthEventPublisher(IntegrationEventPublisher broker)
    {
        _broker = broker;
    }

    public Task HandleAsync(AccountDeleted @event, EventContext context, CancellationToken ct) =>
        _broker.PublishAsync(new AccountDeletedInfo(@event.ClosureId, @event.UserId, @event.DeletedAt), context, ct);

    public Task HandleAsync(AccountClosureRejected @event, EventContext context, CancellationToken ct) =>
        _broker.PublishAsync(new AccountClosureRejectedInfo(@event.ClosureId, @event.UserId, @event.Reason, @event.RejectedAt), context, ct);
}
