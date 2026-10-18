using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
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

        string connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required. Set it via environment or secret.");
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

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
