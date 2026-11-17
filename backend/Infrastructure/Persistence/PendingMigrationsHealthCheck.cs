using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TemplateApp.Infrastructure.Persistence;

internal sealed class PendingMigrationsHealthCheck : IHealthCheck
{
    private readonly AppDbContext _db;

    public PendingMigrationsHealthCheck(AppDbContext db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        HealthCheckConnection.LimitTo(_db.Database, context.Registration);
        int pending = (await _db.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).Count();
        return pending == 0
            ? HealthCheckResult.Healthy()
            : new HealthCheckResult(
                context.Registration.FailureStatus,
                string.Create(CultureInfo.InvariantCulture, $"{pending} pending migrations"));
    }
}
