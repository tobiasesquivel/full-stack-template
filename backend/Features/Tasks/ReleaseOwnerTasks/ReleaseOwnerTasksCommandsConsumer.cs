using TemplateApp.Infrastructure.Messaging;
using TemplateApp.Infrastructure.Messaging.Bus;

namespace TemplateApp.Features.Tasks.ReleaseOwnerTasks;

internal sealed class ReleaseOwnerTasksCommandsConsumer : IIntegrationEventHandler<ReleaseOwnerTasksIntegrationCommand>
{
    private readonly ReleaseOwnerTasks _slice;

    public ReleaseOwnerTasksCommandsConsumer(ReleaseOwnerTasks slice)
    {
        _slice = slice;
    }

    public Task HandleAsync(ReleaseOwnerTasksIntegrationCommand command, EventContext context, CancellationToken ct) =>
        _slice.ExecuteAsync(command.ClosureId, command.UserId, context, ct);
}
