using Microsoft.EntityFrameworkCore;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Health.Probes;

public interface IDatabaseReadinessProbe
{
    Task<ReadinessResult> CheckAsync(CancellationToken cancellationToken = default);
}

public sealed record ReadinessResult(bool Healthy);

public sealed class DatabaseReadinessProbe : IDatabaseReadinessProbe
{
    private readonly AppDbContext _db;

    public DatabaseReadinessProbe(AppDbContext db) => _db = db;

    public async Task<ReadinessResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await _db.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false))
            {
                return new ReadinessResult(false);
            }

            IEnumerable<string> pending = await _db.Database
                .GetPendingMigrationsAsync(cancellationToken)
                .ConfigureAwait(false);

            return new ReadinessResult(!pending.Any());
        }
        catch (Exception)
        {
            return new ReadinessResult(false);
        }
    }
}
