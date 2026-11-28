using TemplateApp.Shared.Domain;

namespace TemplateApp.Tests.Support.Contexts;

internal sealed class ProbeItem : IHasDomainEvents
{
    private readonly List<IDomainEvent> _domainEvents = [];

    private ProbeItem()
    {
    }

    public Guid Id { get; private set; }
    public string HolderId { get; private set; } = string.Empty;

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    public static ProbeItem Create(string holderId)
    {
        ProbeItem item = new() { Id = Guid.NewGuid(), HolderId = holderId };
        item._domainEvents.Add(new ProbeItemCreated(item.Id));
        return item;
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}
