using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TemplateApp.Features.Health.Probes;
namespace TemplateApp.Features.Health;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/api/v1/health").AllowAnonymous();

        group.MapGet("/live", (TimeProvider clock) =>
            Results.Ok(new HealthStatusResponse("live", clock.GetUtcNow())));

        group.MapGet("/ready", async (
            IDatabaseReadinessProbe probe,
            TimeProvider clock,
            CancellationToken cancellationToken) =>
        {
            ReadinessResult result = await probe.CheckAsync(cancellationToken).ConfigureAwait(false);
            var payload = new HealthStatusResponse(
                result.Healthy ? "ready" : "not-ready", clock.GetUtcNow());
            return Results.Json(payload, statusCode: result.Healthy
                ? StatusCodes.Status200OK
                : StatusCodes.Status503ServiceUnavailable);
        });

        return routes;
    }
}

public sealed record HealthStatusResponse(string Status, DateTimeOffset Timestamp);
