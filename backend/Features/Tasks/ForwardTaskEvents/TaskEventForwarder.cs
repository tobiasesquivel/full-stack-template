using TemplateApp.Features.Tasks.Domain.Events;
using TemplateApp.Infrastructure.Messaging;

namespace TemplateApp.Features.Tasks.ForwardTaskEvents;

internal sealed class TaskEventForwarder :
    IEventHandler<TaskCreated>,
    IEventHandler<TaskCompleted>,
    IEventHandler<TaskReopened>,
    IEventHandler<TaskDeleted>
{
    private readonly TaskEventsClient _monolith;

    public TaskEventForwarder(TaskEventsClient monolith)
    {
        _monolith = monolith;
    }

    public Task HandleAsync(TaskCreated @event, EventContext context, CancellationToken ct) => _monolith.SendAsync(@event, context, ct);

    public Task HandleAsync(TaskCompleted @event, EventContext context, CancellationToken ct) => _monolith.SendAsync(@event, context, ct);

    public Task HandleAsync(TaskReopened @event, EventContext context, CancellationToken ct) => _monolith.SendAsync(@event, context, ct);

    public Task HandleAsync(TaskDeleted @event, EventContext context, CancellationToken ct) => _monolith.SendAsync(@event, context, ct);
}
