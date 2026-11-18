using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TemplateApp.Infrastructure.Persistence;

internal sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly PlatformDbContext _db;

    public DatabaseHealthCheck(PlatformDbContext db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        HealthCheckConnection.LimitTo(_db.Database, context.Registration);
        await _db.Database.ExecuteSqlAsync($"SELECT 1", cancellationToken).ConfigureAwait(false);
        return HealthCheckResult.Healthy();
    }
}
