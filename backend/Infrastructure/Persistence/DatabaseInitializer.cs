using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace TemplateApp.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    private const long InitializationLockKey = 7_164_253_001;

    public static async Task InitializeAsync(IServiceProvider rootServices, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rootServices);

        using IServiceScope scope = rootServices.CreateScope();
        IServiceProvider services = scope.ServiceProvider;
        AppDbContext legacy = services.GetRequiredService<AppDbContext>();
        IReadOnlyList<DbContext> contexts = services.MigratedContexts();

        await using NpgsqlConnection lockConnection = new(legacy.Database.GetConnectionString());
        await lockConnection.OpenAsync(ct);
        await using NpgsqlCommand acquire = LockCommand(new NpgsqlCommand("SELECT pg_advisory_lock(@key)", lockConnection));
        await acquire.ExecuteNonQueryAsync(ct);
        try
        {
            if (await IsLegacyDatabaseAsync(lockConnection, ct))
            {
                await legacy.Database.MigrateAsync(ct);
                foreach (DbContext context in services.ContextsThatAdoptLegacyTables())
                    await BaselineAsync(context, lockConnection, ct);
            }

            foreach (DbContext context in contexts)
                await context.Database.MigrateAsync(ct);

            foreach (IDatabaseSeeder seeder in services.GetServices<IDatabaseSeeder>())
                await seeder.SeedAsync(ct);
        }
        finally
        {
            await using NpgsqlCommand release = LockCommand(new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", lockConnection));
            await release.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private static async Task<bool> IsLegacyDatabaseAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using NpgsqlCommand probe = new("SELECT to_regclass(@table) IS NOT NULL", connection);
        probe.Parameters.AddWithValue("table", $"{ContextRegistration.LegacySchema}.\"{ContextRegistration.HistoryTable}\"");
        return await probe.ExecuteScalarAsync(ct) is true;
    }

    private static async Task BaselineAsync(DbContext context, NpgsqlConnection connection, CancellationToken ct)
    {
        IHistoryRepository history = context.GetService<IHistoryRepository>();
        await ExecuteAsync(connection, history.GetCreateIfNotExistsScript(), ct);
        if ((await history.GetAppliedMigrationsAsync(ct)).Count > 0)
            return;

        string initialCreate = context.Database.GetMigrations().First();
        await ExecuteAsync(connection, history.GetInsertScript(new HistoryRow(initialCreate, ProductInfo.GetVersion())), ct);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string script, CancellationToken ct)
    {
        await using NpgsqlCommand command = new(script, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static NpgsqlCommand LockCommand(NpgsqlCommand command)
    {
        command.Parameters.AddWithValue("key", InitializationLockKey);
        return command;
    }
}
