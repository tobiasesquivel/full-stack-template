using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Infrastructure.Messaging;

namespace TemplateApp.Features.Users.CancelAccountClosure;

internal sealed class AccountClosureRejectionsConsumer : IIntegrationEventHandler<AccountClosureRejectedInfo>
{
    private readonly CancelAccountClosure _slice;

    public AccountClosureRejectionsConsumer(CancelAccountClosure slice)
    {
        _slice = slice;
    }

    public Task HandleAsync(AccountClosureRejectedInfo @event, EventContext context, CancellationToken ct) =>
        _slice.ExecuteAsync(@event.ClosureId, @event.UserId, @event.Reason, context, ct);
}
