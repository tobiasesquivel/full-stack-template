using ErrorOr;
using TemplateApp.Infrastructure.Messaging;
using TemplateApp.Infrastructure.Messaging.Bus;

namespace TemplateApp.Features.Projects.ApplyTaskEvent;

internal sealed class TaskEventsConsumer :
    IIntegrationEventHandler<TaskCreatedIntegrationEvent>,
    IIntegrationEventHandler<TaskCompletedIntegrationEvent>,
    IIntegrationEventHandler<TaskReopenedIntegrationEvent>,
    IIntegrationEventHandler<TaskDeletedIntegrationEvent>
{
    private readonly ApplyTaskEvent _slice;

    public TaskEventsConsumer(ApplyTaskEvent slice)
    {
        _slice = slice;
    }

    public Task HandleAsync(TaskCreatedIntegrationEvent @event, EventContext context, CancellationToken ct) =>
        ApplyAsync(TaskEvents.Created, @event.ProjectId, new TaskCountersDelta(1, 0), context, ct);

    public Task HandleAsync(TaskCompletedIntegrationEvent @event, EventContext context, CancellationToken ct) =>
        ApplyAsync(TaskEvents.Completed, @event.ProjectId, new TaskCountersDelta(0, 1), context, ct);

    public Task HandleAsync(TaskReopenedIntegrationEvent @event, EventContext context, CancellationToken ct) =>
        ApplyAsync(TaskEvents.Reopened, @event.ProjectId, new TaskCountersDelta(0, -1), context, ct);

    public Task HandleAsync(TaskDeletedIntegrationEvent @event, EventContext context, CancellationToken ct) =>
        ApplyAsync(TaskEvents.Deleted, @event.ProjectId, new TaskCountersDelta(-1, @event.WasCompleted ? -1 : 0), context, ct);

    private async Task ApplyAsync(string type, Guid? projectId, TaskCountersDelta delta, EventContext context, CancellationToken ct)
    {
        ErrorOr<Success> applied = await _slice.ExecuteAsync(type, projectId, delta, context, ct);
        if (applied.IsError)
            throw new ConsumerUnavailableException($"{type} {context.MessageId} waits: {applied.FirstError.Description}");
    }
}
