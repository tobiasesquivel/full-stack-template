using System.Collections.Concurrent;

namespace TemplateApp.Tests.Messaging;

internal sealed class ConsumerProbe
{
    private readonly ConcurrentQueue<Guid> _calls = new();

    public bool Fails { get; set; }

    public bool UsesInbox { get; set; } = true;

    public IReadOnlyList<Guid> Calls => _calls.ToArray();

    public void Called(Guid messageId) => _calls.Enqueue(messageId);
}
