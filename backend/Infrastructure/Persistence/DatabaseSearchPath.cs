using Microsoft.Extensions.Options;
using Npgsql;

namespace TemplateApp.Infrastructure.Persistence;

public static class DatabaseSearchPath
{
    public static string Of(IServiceProvider services) =>
        string.Join(',', services.GetServices<MigratedContext>()
            .Select(migrated => migrated.Schema)
            .Where(schema => schema != ContextRegistration.LegacySchema)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .Prepend(ContextRegistration.LegacySchema));

    public static string ConnectionString(IServiceProvider services) =>
        new NpgsqlConnectionStringBuilder(services.GetRequiredService<IOptions<DatabaseOptions>>().Value.DefaultConnection)
        {
            SearchPath = Of(services),
        }.ConnectionString;
}
