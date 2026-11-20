using TemplateApp.Features.Tasks.Domain.Events;
using TemplateApp.Infrastructure.Messaging;

namespace TemplateApp.Features.Tasks.PublishTaskEvents;

internal sealed class TaskEventPublisher :
    IEventHandler<TaskCreated>,
    IEventHandler<TaskCompleted>,
    IEventHandler<TaskReopened>,
    IEventHandler<TaskDeleted>,
    IEventHandler<OwnerTasksHeld>,
    IEventHandler<OwnerTasksReleased>
{
    private readonly IntegrationEventPublisher _broker;

    public TaskEventPublisher(IntegrationEventPublisher broker)
    {
        _broker = broker;
    }

    public Task HandleAsync(TaskCreated @event, EventContext context, CancellationToken ct) =>
        _broker.PublishAsync(new TaskCreatedIntegrationEvent(@event.TaskId, @event.OwnerId, @event.ProjectId, @event.CreatedAt), context, ct);

    public Task HandleAsync(TaskCompleted @event, EventContext context, CancellationToken ct) =>
        _broker.PublishAsync(new TaskCompletedIntegrationEvent(@event.TaskId, @event.OwnerId, @event.ProjectId, @event.CompletedAt), context, ct);

    public Task HandleAsync(TaskReopened @event, EventContext context, CancellationToken ct) =>
        _broker.PublishAsync(new TaskReopenedIntegrationEvent(@event.TaskId, @event.OwnerId, @event.ProjectId, @event.ReopenedAt), context, ct);

    public Task HandleAsync(TaskDeleted @event, EventContext context, CancellationToken ct) =>
        _broker.PublishAsync(new TaskDeletedIntegrationEvent(@event.TaskId, @event.OwnerId, @event.ProjectId, @event.WasCompleted), context, ct);

    public Task HandleAsync(OwnerTasksHeld @event, EventContext context, CancellationToken ct) =>
        _broker.PublishAsync(new OwnerTasksHeldIntegrationEvent(@event.ClosureId, @event.OwnerId, @event.HeldAt), context, ct);

    public Task HandleAsync(OwnerTasksReleased @event, EventContext context, CancellationToken ct) =>
        _broker.PublishAsync(new OwnerTasksReleasedIntegrationEvent(@event.ClosureId, @event.OwnerId, @event.ReleasedAt), context, ct);
}
