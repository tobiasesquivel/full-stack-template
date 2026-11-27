using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TemplateApp.Infrastructure.Persistence.Audit;
using TemplateApp.Infrastructure.Persistence.Transactions;

namespace TemplateApp.Infrastructure.Persistence;

public static class ContextRegistration
{
    public const string LegacySchema = "public";
    public const string HistoryTable = "__EFMigrationsHistory";

    public static IServiceCollection AddMigratedDbContext<TContext>(
        this IServiceCollection services,
        string schema,
        bool adoptsLegacyTables,
        bool appliesMigrations = true)
        where TContext : DbContext
    {
        services.AddDbContext<TContext>((provider, options) => options
            .UseNpgsql(
                DatabaseSearchPath.ConnectionString(provider),
                npgsql => npgsql.MigrationsHistoryTable(HistoryTable, schema))
            .AddInterceptors(provider.GetRequiredService<AuditInterceptor>()));
        services.AddSingleton(new MigratedContext(typeof(TContext), schema, adoptsLegacyTables, appliesMigrations));
        return services;
    }

    public static IServiceCollection AddAfterCommit<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.TryAddScoped<AfterCommit>();
        services.ConfigureDbContext<TContext>((provider, options) => options.AddInterceptors(new AfterCommitInterceptor(provider.GetRequiredService<AfterCommit>())));
        return services;
    }

    internal static IServiceCollection AddLegacyDbContext(this IServiceCollection services) =>
        services.AddDbContext<AppDbContext>((provider, options) => options
            .UseNpgsql(
                DatabaseSearchPath.ConnectionString(provider),
                npgsql => npgsql.MigrationsHistoryTable(HistoryTable, LegacySchema))
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning)));

    public static IReadOnlyList<DbContext> MigratedContexts(this IServiceProvider services) =>
        services.GetServices<MigratedContext>()
            .Select(migrated => (DbContext)services.GetRequiredService(migrated.ContextType))
            .ToList();

    public static IReadOnlyList<DbContext> ContextsThisHostMigrates(this IServiceProvider services) =>
        services.GetServices<MigratedContext>()
            .Where(migrated => migrated.AppliesMigrations)
            .Select(migrated => (DbContext)services.GetRequiredService(migrated.ContextType))
            .ToList();

    public static IReadOnlyList<DbContext> ContextsThatAdoptLegacyTables(this IServiceProvider services) =>
        services.GetServices<MigratedContext>()
            .Where(migrated => migrated.AdoptsLegacyTables && migrated.AppliesMigrations)
            .Select(migrated => (DbContext)services.GetRequiredService(migrated.ContextType))
            .ToList();
}
