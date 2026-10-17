using Microsoft.AspNetCore.Http;
using TemplateApp.Features.Health.Probes;
namespace TemplateApp.Features.Health;

public static class HealthEndpoints
{
    internal static IResult Live(TimeProvider clock) =>
        Results.Ok(new HealthStatusResponse("live", clock.GetUtcNow()));

    internal static async Task<IResult> ReadyAsync(
        IDatabaseReadinessProbe probe,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ReadinessResult result = await probe.CheckAsync(cancellationToken).ConfigureAwait(false);
        var payload = new HealthStatusResponse(
            result.Healthy ? "ready" : "not-ready", clock.GetUtcNow());
        return Results.Json(payload, statusCode: result.Healthy
            ? StatusCodes.Status200OK
            : StatusCodes.Status503ServiceUnavailable);
    }
}

public sealed record HealthStatusResponse(string Status, DateTimeOffset Timestamp);
