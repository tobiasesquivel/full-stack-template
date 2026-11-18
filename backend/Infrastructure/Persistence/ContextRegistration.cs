using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace TemplateApp.Infrastructure.Persistence;

public static class ContextRegistration
{
    public const string LegacySchema = "public";
    public const string HistoryTable = "__EFMigrationsHistory";

    public static IServiceCollection AddMigratedDbContext<TContext>(this IServiceCollection services, string schema)
        where TContext : DbContext
    {
        services.AddDbContext<TContext>((provider, options) => options
            .UseNpgsql(
                ConnectionString(provider),
                npgsql => npgsql.MigrationsHistoryTable(HistoryTable, schema))
            .AddInterceptors(provider.GetRequiredService<AuditInterceptor>()));
        services.AddSingleton(new MigratedContext(typeof(TContext)));
        return services;
    }

    internal static IServiceCollection AddLegacyDbContext(this IServiceCollection services) =>
        services.AddDbContext<AppDbContext>((provider, options) => options
            .UseNpgsql(
                ConnectionString(provider),
                npgsql => npgsql.MigrationsHistoryTable(HistoryTable, LegacySchema))
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning)));

    private static string ConnectionString(IServiceProvider provider) =>
        DatabaseSearchPath.Apply(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.DefaultConnection);

    public static IReadOnlyList<DbContext> MigratedContexts(this IServiceProvider services) =>
        services.GetServices<MigratedContext>()
            .Select(migrated => (DbContext)services.GetRequiredService(migrated.ContextType))
            .ToList();
}
