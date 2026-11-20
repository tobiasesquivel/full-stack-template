using TemplateApp.Infrastructure.Messaging;

namespace TemplateApp.Features.Tasks.DeleteOwnedTasks;

internal sealed class UserDeletionsConsumer : IIntegrationEventHandler<UserDeletedIntegrationEvent>
{
    private readonly DeleteOwnedTasks _slice;

    public UserDeletionsConsumer(DeleteOwnedTasks slice)
    {
        _slice = slice;
    }

    public Task HandleAsync(UserDeletedIntegrationEvent @event, EventContext context, CancellationToken ct) =>
        _slice.ExecuteAsync(@event.UserId, @event.DeletedAt, context, ct);
}
