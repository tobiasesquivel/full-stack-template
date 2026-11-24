using TemplateApp.Infrastructure.Messaging;
using TemplateApp.Infrastructure.Messaging.Bus;

namespace TemplateApp.Features.Tasks.HoldOwnerTasks;

internal sealed class HoldOwnerTasksCommandsConsumer : IIntegrationEventHandler<HoldOwnerTasksIntegrationCommand>
{
    private readonly HoldOwnerTasks _slice;

    public HoldOwnerTasksCommandsConsumer(HoldOwnerTasks slice)
    {
        _slice = slice;
    }

    public Task HandleAsync(HoldOwnerTasksIntegrationCommand command, EventContext context, CancellationToken ct) =>
        _slice.ExecuteAsync(command.ClosureId, command.UserId, context, ct);
}
