using TemplateApp.Features.Users.Domain.Events;
using TemplateApp.Shared.Domain;

namespace TemplateApp.Features.Users.Domain;

internal sealed class UserProfile : IOwnedResource, IAuditable, IHasDomainEvents
{
    public const int DisplayNameMaxLength = 200;

    private readonly List<IDomainEvent> _domainEvents = [];

    public required string AuthId { get; init; }
    public required string DisplayName { get; set; }
    public string Timezone { get; set; } = "UTC";
    public AccountClosureStatus ClosureStatus { get; private set; }
    public Guid? ClosureId { get; private set; }
    public string? ClosureReason { get; private set; }
    public DateTimeOffset? ClosureChangedAt { get; private set; }
    public DateTimeOffset? ClosesAt { get; private set; }

    public string OwnerId => AuthId;

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    public bool ScheduleClosure(Guid closureId, DateTimeOffset now, DateTimeOffset closesAt)
    {
        if (ClosureStatus is AccountClosureStatus.Scheduled or AccountClosureStatus.Closing)
            return false;

        ClosureStatus = AccountClosureStatus.Scheduled;
        ClosureId = closureId;
        ClosureReason = null;
        ClosureChangedAt = now;
        ClosesAt = closesAt;
        return true;
    }

    public bool WithdrawClosure(Guid closureId, DateTimeOffset now)
    {
        if (ClosureStatus != AccountClosureStatus.Scheduled || ClosureId != closureId)
            return false;

        ClosureStatus = AccountClosureStatus.Cancelled;
        ClosureChangedAt = now;
        return true;
    }

    public bool RequestClosure(Guid closureId, DateTimeOffset now)
    {
        if (ClosureStatus == AccountClosureStatus.Closing
            || (ClosureStatus == AccountClosureStatus.Scheduled && ClosureId != closureId))
            return false;

        ClosureStatus = AccountClosureStatus.Closing;
        ClosureId = closureId;
        ClosureReason = null;
        ClosureChangedAt = now;
        return true;
    }

    public bool RejectClosure(Guid closureId, string reason, DateTimeOffset now)
    {
        if (ClosureStatus != AccountClosureStatus.Closing || ClosureId != closureId)
            return false;

        ClosureStatus = AccountClosureStatus.Rejected;
        ClosureReason = reason;
        ClosureChangedAt = now;
        return true;
    }

    public void MarkDeleted(DateTimeOffset now) => _domainEvents.Add(new UserDeleted(AuthId, now));

    public void ClearDomainEvents() => _domainEvents.Clear();

    public static bool IsValidDisplayName(string displayName) =>
        NormalizeDisplayName(displayName).Length is >= 1 and <= DisplayNameMaxLength;

    public static string NormalizeDisplayName(string displayName) => displayName.Trim();
}
