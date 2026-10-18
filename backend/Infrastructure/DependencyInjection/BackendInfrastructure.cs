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

        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.Section))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();
        services.AddDbContext<AppDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.DefaultConnection));

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
