using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Features.Users.OrchestrateAccountClosure;
using TemplateApp.Features.Users.Persistence;

namespace TemplateApp.Features.Users.GetAccountClosure;

internal sealed class GetAccountClosure
{
    private readonly UsersDbContext _db;
    private readonly IAuthModule _auth;

    public GetAccountClosure(UsersDbContext db, IAuthModule auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<ErrorOr<AccountClosureStatusResponse>> ExecuteAsync(string callerId, Guid closureId, CancellationToken ct)
    {
        var closure = await _db.AccountClosureSagas
            .Where(s => s.Id == closureId)
            .Select(s => new { s.Id, s.UserId, s.State, s.Reason, s.Overdue, s.StartedAt, s.StateChangedAt, s.FinishedAt })
            .SingleOrDefaultAsync(ct);
        if (closure is null)
            return UserErrors.ClosureNotFound(closureId);

        if (!await _auth.CanAccessAsync(callerId, new ClosureOwner(closure.UserId), UserPermissions.Read, ct))
            return UserErrors.Forbidden;

        return new AccountClosureStatusResponse(
            closure.Id,
            closure.UserId,
            Describe(closure.State),
            closure.Reason,
            closure.Overdue,
            closure.StartedAt,
            closure.StateChangedAt,
            closure.FinishedAt);
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
