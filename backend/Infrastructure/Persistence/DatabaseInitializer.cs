using Microsoft.EntityFrameworkCore;
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
        AppDbContext db = services.GetRequiredService<AppDbContext>();

        await using NpgsqlConnection lockConnection = new(db.Database.GetConnectionString());
        await lockConnection.OpenAsync(ct);
        await using NpgsqlCommand acquire = LockCommand(new NpgsqlCommand("SELECT pg_advisory_lock(@key)", lockConnection));
        await acquire.ExecuteNonQueryAsync(ct);
        try
        {
            await db.Database.MigrateAsync(ct);
            foreach (IDatabaseSeeder seeder in services.GetServices<IDatabaseSeeder>())
                await seeder.SeedAsync(ct);
        }
        finally
        {
            await using NpgsqlCommand release = LockCommand(new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", lockConnection));
            await release.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private static NpgsqlCommand LockCommand(NpgsqlCommand command)
    {
        command.Parameters.AddWithValue("key", InitializationLockKey);
        return command;
    }
}
