using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Infrastructure.Messaging;

namespace TemplateApp.Features.Users.CompleteAccountClosure;

internal sealed class AccountDeletionsConsumer : IIntegrationEventHandler<AccountDeletedInfo>
{
    private readonly CompleteAccountClosure _slice;

    public AccountDeletionsConsumer(CompleteAccountClosure slice)
    {
        _slice = slice;
    }

    public Task HandleAsync(AccountDeletedInfo @event, EventContext context, CancellationToken ct) =>
        _slice.ExecuteAsync(@event.ClosureId, @event.UserId, context, ct);
}
