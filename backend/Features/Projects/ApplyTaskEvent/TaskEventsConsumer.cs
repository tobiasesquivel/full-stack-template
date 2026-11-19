using ErrorOr;
using TemplateApp.Infrastructure.Messaging;

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
        ApplyAsync(TaskEvents.Created, new TaskEventPayload(@event.ProjectId, WasCompleted: false), context, ct);

    public Task HandleAsync(TaskCompletedIntegrationEvent @event, EventContext context, CancellationToken ct) =>
        ApplyAsync(TaskEvents.Completed, new TaskEventPayload(@event.ProjectId, WasCompleted: false), context, ct);

    public Task HandleAsync(TaskReopenedIntegrationEvent @event, EventContext context, CancellationToken ct) =>
        ApplyAsync(TaskEvents.Reopened, new TaskEventPayload(@event.ProjectId, WasCompleted: false), context, ct);

    public Task HandleAsync(TaskDeletedIntegrationEvent @event, EventContext context, CancellationToken ct) =>
        ApplyAsync(TaskEvents.Deleted, new TaskEventPayload(@event.ProjectId, @event.WasCompleted), context, ct);

    private async Task ApplyAsync(string type, TaskEventPayload payload, EventContext context, CancellationToken ct)
    {
        ErrorOr<Success> applied = await _slice.ExecuteAsync(
            context.MessageId,
            new TaskEventRequest(type, TaskEvents.Version, context.OccurredAt, payload),
            ct);
        if (!applied.IsError)
            return;

        if (applied.FirstError.Code == ProjectErrors.TaskCountersRebuilding.Code)
            throw new ConsumerUnavailableException($"{type} {context.MessageId} waits: {applied.FirstError.Description}");

        throw new UnreadableMessageException($"{type} {context.MessageId}: {applied.FirstError.Description}");
    }
}
