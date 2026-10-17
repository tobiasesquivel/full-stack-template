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

        return services;
    }
}
