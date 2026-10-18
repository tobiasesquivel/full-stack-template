using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TemplateApp.Infrastructure.Middlewares;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Infrastructure.DependencyInjection;

public static class BackendInfrastructure
{
    public static IServiceCollection AddBackendInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.AddOptionsWithValidateOnStart<DatabaseOptions, DatabaseOptionsValidator>()
            .BindConfiguration(DatabaseOptions.Section);
        services.AddDbContext<AppDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.DefaultConnection));

        services.AddHostedService<DatabaseInitialization>();

        services.AddSingleton(TimeProvider.System);
        services.AddDataProtectionKeys(configuration);

        return services;
    }

    private static IServiceCollection AddDataProtectionKeys(this IServiceCollection services, IConfiguration configuration)
    {
        IDataProtectionBuilder dataProtection = services.AddDataProtection().SetApplicationName("TemplateApp");

        string? keysDirectory = configuration["DataProtection:KeysDirectory"];
        if (!string.IsNullOrWhiteSpace(keysDirectory))
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysDirectory));

        return services;
    }
}
