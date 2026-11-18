using Microsoft.EntityFrameworkCore;

namespace TemplateApp.Infrastructure.Persistence;

internal sealed class IdempotencyKeys
{
    private readonly PlatformDbContext _db;

    public IdempotencyKeys(PlatformDbContext db)
    {
        _db = db;
    }

    public Task<int> ForgetOwnerAsync(string ownerId, CancellationToken ct) =>
        _db.IdempotencyKeys.Where(k => k.OwnerId == ownerId).ExecuteDeleteAsync(ct);
}
