using Microsoft.EntityFrameworkCore;

namespace TemplateApp.Infrastructure.Persistence;

internal sealed class IdempotencyKeys
{
    private readonly AppDbContext _db;

    public IdempotencyKeys(AppDbContext db)
    {
        _db = db;
    }

    public Task<int> ForgetOwnerAsync(string ownerId, CancellationToken ct) =>
        _db.IdempotencyKeys.Where(k => k.OwnerId == ownerId).ExecuteDeleteAsync(ct);
}
