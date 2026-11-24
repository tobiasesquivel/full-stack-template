using System.Globalization;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Users.Access;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Features.Users.OrchestrateAccountClosure;
using TemplateApp.Features.Users.Persistence;

namespace TemplateApp.Features.Users.GetAccountClosure;

internal static class GetAccountClosure
{
    public sealed class Handler
    {
        private readonly UsersDbContext _db;
        private readonly IAuthModule _auth;

        public Handler(UsersDbContext db, IAuthModule auth)
        {
            _db = db;
            _auth = auth;
        }

        public async Task<ErrorOr<AccountClosureStatusResponse>> Handle(GetAccountClosureCommand command, CancellationToken ct)
        {
            Guid closureId = command.ClosureId;
            var closure = await _db.AccountClosureSagas
                .Where(s => s.Id == closureId)
                .Select(s => new { s.Id, s.UserId, s.State, s.Reason, s.Overdue, s.StartedAt, s.StateChangedAt, s.FinishedAt })
                .SingleOrDefaultAsync(ct);
            var facts = await _db.AccountClosureHistory
                .Where(e => e.ClosureId == closureId && e.Kind != AccountClosureFacts.ReminderSent)
                .OrderBy(e => e.Id)
                .Select(e => new { e.UserId, e.Kind, e.Detail, e.OccurredAt })
                .ToListAsync(ct);
            if (closure is null && facts.Count == 0)
                return UserErrors.ClosureNotFound(closureId);

            string ownerId = closure?.UserId ?? facts[0].UserId;
            if (!await _auth.CanAccessAsync(command.CallerId, new ClosureOwner(ownerId), UserPermissions.Read, ct))
                return UserErrors.Forbidden;

            var requested = facts.Find(f => f.Kind == AccountClosureFacts.Requested);
            DateTimeOffset? closesAt = requested?.Detail is { } detail
                ? DateTimeOffset.Parse(detail, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                : null;
            if (closure is not null)
            {
                return new AccountClosureStatusResponse(
                    closure.Id,
                    closure.UserId,
                    Describe(closure.State),
                    closure.Reason,
                    closure.Overdue,
                    closure.StartedAt,
                    closure.StateChangedAt,
                    closure.FinishedAt,
                    closesAt);
            }

            var cancelled = facts.Find(f => f.Kind == AccountClosureFacts.Cancelled);
            return new AccountClosureStatusResponse(
                closureId,
                ownerId,
                cancelled is null ? "scheduled" : "cancelled",
                null,
                false,
                (requested ?? facts[0]).OccurredAt,
                facts[^1].OccurredAt,
                cancelled?.OccurredAt,
                closesAt);
        }
    }

    private static string Describe(AccountClosureState state) => state switch
    {
        AccountClosureState.HoldingTasks => "holdingTasks",
        AccountClosureState.Deciding => "deciding",
        AccountClosureState.ReleasingTasks => "releasingTasks",
        AccountClosureState.Completed => "completed",
        AccountClosureState.Rejected => "rejected",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown closure state."),
    };
}
