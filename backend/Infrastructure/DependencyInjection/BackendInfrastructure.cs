using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TemplateApp.Infrastructure.Middlewares;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Infrastructure.DependencyInjection;

public static class BackendInfrastructure
{
    public static IServiceCollection AddBackendInfrastructure(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.AddOptionsWithValidateOnStart<DatabaseOptions, DatabaseOptionsValidator>()
            .BindConfiguration(DatabaseOptions.Section);
        services.AddDbContext<AppDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.DefaultConnection));

        services.AddHostedService<DatabaseInitialization>();

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
