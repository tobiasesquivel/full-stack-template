using TemplateApp.Features.Health.Contracts;

namespace TemplateApp.Features.Health.GetReadiness;

public static class GetReadinessEndpoint
{
    public static void Map(IEndpointRouteBuilder health) => health.MapGet("/ready", HandleAsync);

    private static async Task<IResult> HandleAsync(
        GetReadiness slice,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        bool ready = await slice.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        var payload = new HealthStatusResponse(ready ? "ready" : "not-ready", clock.GetUtcNow());
        return Results.Json(payload, statusCode: ready
            ? StatusCodes.Status200OK
            : StatusCodes.Status503ServiceUnavailable);
    }
}
