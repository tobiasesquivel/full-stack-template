using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace TemplateApp.Infrastructure.Persistence.Health;

public static class HealthCheckConnection
{
    private const int MaxConnections = 2;

    public static void LimitTo(DatabaseFacade database, HealthCheckRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(registration);
        if (registration.Timeout == Timeout.InfiniteTimeSpan)
            return;

        int seconds = (int)Math.Ceiling(registration.Timeout.TotalSeconds);
        database.SetConnectionString(new NpgsqlConnectionStringBuilder(database.GetConnectionString())
        {
            Timeout = seconds,
            CommandTimeout = seconds,
            MinPoolSize = 0,
            MaxPoolSize = MaxConnections,
        }.ConnectionString);
    }
}
