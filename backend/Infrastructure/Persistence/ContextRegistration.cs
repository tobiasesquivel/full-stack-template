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
        if (!IsPlainSchemaName(schema))
            throw new ArgumentException($"The schema of {typeof(TContext).Name} must be lowercase letters, digits and underscores, starting with a letter or underscore: PostgreSQL folds any other name in the Search Path.", nameof(schema));

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

    private static bool IsPlainSchemaName(string schema) =>
        schema.Length > 0
        && schema[0] is (>= 'a' and <= 'z') or '_'
        && schema.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_');

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
