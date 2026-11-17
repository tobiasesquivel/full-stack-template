namespace TemplateApp.Features.Health;

public static class HealthFeature
{
    public static IServiceCollection AddHealthFeature(this IServiceCollection services)
    {
        services.AddHealthChecks();
        return services;
    }

    public static RouteGroupBuilder MapHealthEndpoints(this RouteGroupBuilder api)
    {
        RouteGroupBuilder health = api.MapGroup("/health").AllowAnonymous().DisableRateLimiting();

        GetLiveness.GetLivenessEndpoint.Map(health);
        GetReadiness.GetReadinessEndpoint.Map(health);

        return api;
    }
}
