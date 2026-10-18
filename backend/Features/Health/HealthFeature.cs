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

    public static RouteGroupBuilder MapHealthEndpoints(this RouteGroupBuilder api)
    {
        RouteGroupBuilder health = api.MapGroup("/health").AllowAnonymous();

        GetLiveness.GetLivenessEndpoint.Map(health);
        GetReadiness.GetReadinessEndpoint.Map(health);

        return api;
    }
}
