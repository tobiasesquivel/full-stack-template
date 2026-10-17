namespace TemplateApp.Features.Health;

public static class HealthFeature
{
    public static IServiceCollection AddHealthFeature(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        _ = configuration;
        services.AddScoped<GetReadiness.GetReadiness>();
        return services;
    }

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder health = routes.MapGroup("/api/v1/health").AllowAnonymous();

        GetLiveness.GetLivenessEndpoint.Map(health);
        GetReadiness.GetReadinessEndpoint.Map(health);

        return routes;
    }
}
