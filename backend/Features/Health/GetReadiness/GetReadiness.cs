using Microsoft.EntityFrameworkCore;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Health.GetReadiness;

public sealed class GetReadiness
{
    private readonly AppDbContext _db;

    public GetReadiness(AppDbContext db) => _db = db;

    public async Task<bool> ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!await _db.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            IEnumerable<string> pending = await _db.Database
                .GetPendingMigrationsAsync(cancellationToken)
                .ConfigureAwait(false);

            return !pending.Any();
        }
        catch (Exception)
        {
            return false;
        }
    }
}
