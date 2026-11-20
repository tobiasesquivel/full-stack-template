using TemplateApp.Features.Tasks.Domain.Events;
using TemplateApp.Shared.Domain;

namespace TemplateApp.Features.Tasks.Domain;

internal sealed class OwnerHold : IHasDomainEvents
{
    private readonly List<IDomainEvent> _domainEvents = [];

    private OwnerHold()
    {
    }

    public string OwnerId { get; private set; } = string.Empty;
    public Guid ClosureId { get; private set; }
    public DateTimeOffset HeldAt { get; private set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    public static OwnerHold Hold(string ownerId, Guid closureId, DateTimeOffset now)
    {
        OwnerHold hold = new() { OwnerId = ownerId };
        hold.Renew(closureId, now);
        return hold;
    }

    public void Renew(Guid closureId, DateTimeOffset now)
    {
        ClosureId = closureId;
        HeldAt = now;
        _domainEvents.Add(new OwnerTasksHeld(closureId, OwnerId, now));
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}
