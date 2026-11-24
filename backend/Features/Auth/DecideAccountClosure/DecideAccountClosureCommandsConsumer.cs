using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Infrastructure.Messaging;
using TemplateApp.Infrastructure.Messaging.Bus;

namespace TemplateApp.Features.Auth.DecideAccountClosure;

internal sealed class DecideAccountClosureCommandsConsumer : IIntegrationEventHandler<DecideAccountClosureInfo>
{
    private readonly DecideAccountClosure _slice;

    public DecideAccountClosureCommandsConsumer(DecideAccountClosure slice)
    {
        _slice = slice;
    }

    public Task HandleAsync(DecideAccountClosureInfo command, EventContext context, CancellationToken ct) =>
        _slice.ExecuteAsync(command.ClosureId, command.UserId, context, ct);
}
