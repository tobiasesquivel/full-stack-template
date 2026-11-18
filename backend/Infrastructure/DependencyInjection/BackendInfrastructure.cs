using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Options;
using TemplateApp.Infrastructure.Middlewares;
using TemplateApp.Infrastructure.Persistence;
using TemplateApp.Shared.Http;

namespace TemplateApp.Infrastructure.DependencyInjection;

public static class BackendInfrastructure
{
    private static readonly TimeSpan HealthCheckTimeout = TimeSpan.FromSeconds(2);

    public static IServiceCollection AddBackendInfrastructure(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            context.ProblemDetails.Extensions.TryAdd(
                "code",
                HttpErrors.CodeFor(context.ProblemDetails.Status ?? context.HttpContext.Response.StatusCode)));
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.Configure<ExceptionHandlerOptions>(options => options.SuppressDiagnosticsCallback = _ => false);

        services.AddOptionsWithValidateOnStart<DatabaseOptions, DatabaseOptionsValidator>()
            .BindConfiguration(DatabaseOptions.Section);
        services.AddHttpContextAccessor();
        services.AddSingleton<AuditInterceptor>();
        services.AddLegacyDbContext();
        services.AddMigratedDbContext<PlatformDbContext>("platform");
        services.AddScoped<IdempotencyKeys>();

        services.AddHostedService<DatabaseInitialization>();
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", timeout: HealthCheckTimeout)
            .AddCheck<PendingMigrationsHealthCheck>("migrations", timeout: HealthCheckTimeout);

        services.AddSingleton(TimeProvider.System);
        services.AddDataProtectionKeys();

        return services;
    }

    private static IServiceCollection AddDataProtectionKeys(this IServiceCollection services)
    {
        services.AddDataProtection().SetApplicationName("TemplateApp");

        services.AddOptionsWithValidateOnStart<DataProtectionKeysOptions, DataProtectionKeysOptionsValidator>()
            .BindConfiguration(DataProtectionKeysOptions.Section);
        services.AddOptions<KeyManagementOptions>()
            .Configure<IOptions<DataProtectionKeysOptions>, ILoggerFactory>((keyManagement, keys, loggers) =>
            {
                if (!string.IsNullOrWhiteSpace(keys.Value.KeysDirectory))
                    keyManagement.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(keys.Value.KeysDirectory), loggers);
            });

        return services;
    }
}
