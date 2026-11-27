using Microsoft.Extensions.Diagnostics.HealthChecks;
using TemplateApp.Shared.Architecture;

namespace TemplateApp.Features.Health.GetReadiness;

[DuneExemption(DuneCheck.MediatorEndpoint, "La sonda de disponibilidad corre los health checks del host, sin caso de uso.")]
internal static class GetReadinessEndpoint
{
    public static void Map(IEndpointRouteBuilder health) => health.MapGet("/ready", HandleAsync);

    private static async Task<IResult> HandleAsync(
        HealthCheckService checks,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        HealthReport report = await checks.CheckHealthAsync(cancellationToken).ConfigureAwait(false);
        bool ready = report.Status != HealthStatus.Unhealthy;
        var payload = new ReadinessResponse(
            ready ? "ready" : "not-ready",
            clock.GetUtcNow(),
            report.Entries.ToDictionary(entry => entry.Key, entry => StatusName(entry.Value.Status)));
        return Results.Json(payload, statusCode: ready
            ? StatusCodes.Status200OK
            : StatusCodes.Status503ServiceUnavailable);
    }

    private static string StatusName(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => "healthy",
        HealthStatus.Degraded => "degraded",
        _ => "unhealthy",
    };
}
