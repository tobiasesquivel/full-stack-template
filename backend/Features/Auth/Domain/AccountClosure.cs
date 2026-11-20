using TemplateApp.Features.Auth.Domain.Events;
using TemplateApp.Shared.Domain;

namespace TemplateApp.Features.Auth.Domain;

internal sealed class AccountClosure : IHasDomainEvents
{
    private readonly List<IDomainEvent> _domainEvents = [];

    private AccountClosure()
    {
    }

    public Guid ClosureId { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public AccountClosureOutcome Outcome { get; private set; }
    public string? Reason { get; private set; }
    public DateTimeOffset DecidedAt { get; private set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    public static AccountClosure Complete(Guid closureId, string userId, DateTimeOffset now)
    {
        AccountClosure closure = new() { ClosureId = closureId, UserId = userId, Outcome = AccountClosureOutcome.Completed, DecidedAt = now };
        closure._domainEvents.Add(new AccountDeleted(closureId, userId, now));
        return closure;
    }

    public static AccountClosure Reject(Guid closureId, string userId, string reason, DateTimeOffset now)
    {
        AccountClosure closure = new() { ClosureId = closureId, UserId = userId, Outcome = AccountClosureOutcome.Rejected, Reason = reason, DecidedAt = now };
        closure._domainEvents.Add(new AccountClosureRejected(closureId, userId, reason, now));
        return closure;
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}
