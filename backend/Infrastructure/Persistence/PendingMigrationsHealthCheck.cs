using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TemplateApp.Infrastructure.Persistence;

internal sealed class PendingMigrationsHealthCheck : IHealthCheck
{
    private readonly IServiceProvider _services;

    public PendingMigrationsHealthCheck(IServiceProvider services) => _services = services;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        int pending = 0;
        foreach (DbContext migrated in _services.MigratedContexts())
        {
            HealthCheckConnection.LimitTo(migrated.Database, context.Registration);
            pending += (await migrated.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).Count();
        }

        return pending == 0
            ? HealthCheckResult.Healthy()
            : new HealthCheckResult(
                context.Registration.FailureStatus,
                string.Create(CultureInfo.InvariantCulture, $"{pending} pending migrations"));
    }
}
