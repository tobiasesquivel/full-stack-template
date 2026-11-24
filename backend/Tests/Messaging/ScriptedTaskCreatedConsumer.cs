using TemplateApp.Features.Projects.ApplyTaskEvent;
using TemplateApp.Infrastructure.Messaging;
using TemplateApp.Infrastructure.Messaging.Bus;

namespace TemplateApp.Tests.Messaging;

internal sealed class ScriptedTaskCreatedConsumer : IIntegrationEventHandler<TaskCreatedIntegrationEvent>
{
    private readonly ConsumerScript _script;
    private readonly string _host;
    private readonly IIntegrationEventHandler<TaskCreatedIntegrationEvent> _real;

    public ScriptedTaskCreatedConsumer(ConsumerScript script, string host, IIntegrationEventHandler<TaskCreatedIntegrationEvent> real)
    {
        _script = script;
        _host = host;
        _real = real;
    }

    public async Task HandleAsync(TaskCreatedIntegrationEvent @event, EventContext context, CancellationToken ct)
    {
        _script.Called(_host, context.MessageId);
        if (_script.FailuresLeft > 0)
        {
            _script.FailuresLeft--;
            throw new ConsumerUnavailableException("The test consumer is down.");
        }

        await _real.HandleAsync(@event, context, ct);
        if (_script.HoldAfterTheEffect is { } hold)
        {
            _script.HoldAfterTheEffect = null;
            _script.EffectApplied.TrySetResult();
            await hold.Task;
        }
    }
}
