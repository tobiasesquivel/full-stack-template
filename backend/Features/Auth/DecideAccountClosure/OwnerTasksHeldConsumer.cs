using TemplateApp.Infrastructure.Messaging;

namespace TemplateApp.Features.Auth.DecideAccountClosure;

internal sealed class OwnerTasksHeldConsumer : IIntegrationEventHandler<OwnerTasksHeldIntegrationEvent>
{
    private readonly DecideAccountClosure _slice;

    public OwnerTasksHeldConsumer(DecideAccountClosure slice)
    {
        _slice = slice;
    }

    public Task HandleAsync(OwnerTasksHeldIntegrationEvent @event, EventContext context, CancellationToken ct) =>
        _slice.ExecuteAsync(@event.ClosureId, @event.UserId, context, ct);
}
