using System.Transactions;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Users.Persistence;

namespace TemplateApp.Features.Users;

internal sealed class AccountClosureHistory
{
    private readonly UsersDbContext _db;

    public AccountClosureHistory(UsersDbContext db)
    {
        _db = db;
    }

    public async Task<bool> RecordAsync(Guid closureId, string userId, string kind, string actor, string? detail, DateTimeOffset occurredAt, CancellationToken ct)
    {
        if (_db.Database.CurrentTransaction is null && Transaction.Current is null)
            throw new InvalidOperationException("A closure history row is saved in the transaction of the change it records: open it first.");

        int inserted = await _db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO users."AccountClosureHistory" ("ClosureId", "UserId", "Kind", "OccurredAt", "Actor", "Detail")
            VALUES ({closureId}, {userId}, {kind}, {occurredAt}, {actor}, {detail})
            ON CONFLICT ("ClosureId", "Kind", "Detail") DO NOTHING
            """,
            ct);
        return inserted == 1;
    }
}
