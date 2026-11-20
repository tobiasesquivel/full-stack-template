using System.Collections.Concurrent;

namespace TemplateApp.Tests.Messaging;

internal sealed class ConsumerScript
{
    private readonly ConcurrentQueue<(string Host, Guid MessageId)> _calls = new();

    public int FailuresLeft { get; set; }

    public TaskCompletionSource? HoldAfterTheEffect { get; set; }

    public TaskCompletionSource EffectApplied { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<(string Host, Guid MessageId)> Calls => _calls.ToArray();

    public void Called(string host, Guid messageId) => _calls.Enqueue((host, messageId));
}
