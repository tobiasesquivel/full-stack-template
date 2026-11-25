using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Users.Access;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Features.Users.Persistence;

namespace TemplateApp.Features.Users.GetAccountClosureHistory;

internal sealed partial record GetAccountClosureHistoryQuery
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

        public async Task<ErrorOr<AccountClosureHistoryResponse>> Handle(GetAccountClosureHistoryQuery query, CancellationToken ct)
        {
            Guid closureId = query.ClosureId;
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

            if (!await _auth.CanAccessAsync(query.CallerId, new ClosureOwner(ownerId), UserPermissions.Read, ct))
                return UserErrors.Forbidden;

            return new AccountClosureHistoryResponse(closureId, [.. facts.Select(f => f.Fact)]);
        }
    }
}
