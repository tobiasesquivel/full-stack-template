using TemplateApp.Infrastructure.Messaging;

namespace TemplateApp.Features.Tasks.ReleaseOwnerTasks;

internal sealed class AccountClosureRejectionsConsumer : IIntegrationEventHandler<AccountClosureRejectedIntegrationEvent>
{
    private readonly ReleaseOwnerTasks _slice;

    public AccountClosureRejectionsConsumer(ReleaseOwnerTasks slice)
    {
        _slice = slice;
    }

    public Task HandleAsync(AccountClosureRejectedIntegrationEvent @event, EventContext context, CancellationToken ct) =>
        _slice.ExecuteAsync(@event.ClosureId, @event.UserId, context, ct);
}
