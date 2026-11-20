using TemplateApp.Infrastructure.Messaging;

namespace TemplateApp.Features.Tasks.HoldOwnerTasks;

internal sealed class AccountClosureRequestsConsumer : IIntegrationEventHandler<AccountClosureRequestedIntegrationEvent>
{
    private readonly HoldOwnerTasks _slice;

    public AccountClosureRequestsConsumer(HoldOwnerTasks slice)
    {
        _slice = slice;
    }

    public Task HandleAsync(AccountClosureRequestedIntegrationEvent @event, EventContext context, CancellationToken ct) =>
        _slice.ExecuteAsync(@event.ClosureId, @event.UserId, context, ct);
}
