using TemplateApp.Infrastructure.Hosting;

namespace TemplateApp.Features.Health;

internal sealed class HealthFeature : IFeature
{
    public void AddServices(IServiceCollection services)
    {
        services.AddHealthChecks();
    }

    public void MapEndpoints(RouteGroupBuilder api)
    {
        RouteGroupBuilder health = api.MapGroup("/health").AllowAnonymous().DisableRateLimiting();

        GetLiveness.GetLivenessEndpoint.Map(health);
        GetReadiness.GetReadinessEndpoint.Map(health);
    }
}
