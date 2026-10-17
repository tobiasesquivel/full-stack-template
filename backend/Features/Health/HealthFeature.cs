using TemplateApp.Features.Health.Probes;

namespace TemplateApp.Features.Health;

public static class HealthFeature
{
    public static IServiceCollection AddHealthFeature(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        _ = configuration;
        services.AddScoped<IDatabaseReadinessProbe, DatabaseReadinessProbe>();
        return services;
    }

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/api/v1/health").AllowAnonymous();

        group.MapGet("/live", HealthEndpoints.Live);
        group.MapGet("/ready", HealthEndpoints.ReadyAsync);

        return routes;
    }
}
