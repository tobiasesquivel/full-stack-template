using TemplateApp.Features.Health.Probes;

namespace TemplateApp.Features.Health;

public static class HealthFeatureExtensions
{
    public static IServiceCollection AddHealthFeature(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        _ = configuration;
        services.AddScoped<IDatabaseReadinessProbe, DatabaseReadinessProbe>();
        return services;
    }
}
