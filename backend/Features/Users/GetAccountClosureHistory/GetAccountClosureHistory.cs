using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Features.Users.Persistence;

namespace TemplateApp.Features.Users.GetAccountClosureHistory;

internal sealed class GetAccountClosureHistory
{
    private readonly UsersDbContext _db;
    private readonly IAuthModule _auth;

    public GetAccountClosureHistory(UsersDbContext db, IAuthModule auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<ErrorOr<AccountClosureHistoryResponse>> ExecuteAsync(string callerId, Guid closureId, CancellationToken ct)
    {
        var facts = await _db.AccountClosureHistory
            .Where(e => e.ClosureId == closureId)
            .OrderBy(e => e.Id)
            .Select(e => new { e.UserId, Fact = new AccountClosureFactResponse(e.Kind, e.OccurredAt, e.Actor, e.Detail) })
            .ToListAsync(ct);
        string? ownerId = facts.Count > 0
            ? facts[0].UserId
            : await _db.AccountClosureSagas.Where(s => s.Id == closureId).Select(s => s.UserId).SingleOrDefaultAsync(ct);
        if (ownerId is null)
            return UserErrors.ClosureNotFound(closureId);

        if (!await _auth.CanAccessAsync(callerId, new ClosureOwner(ownerId), UserPermissions.Read, ct))
            return UserErrors.Forbidden;

        return new AccountClosureHistoryResponse(closureId, [.. facts.Select(f => f.Fact)]);
    }
}
