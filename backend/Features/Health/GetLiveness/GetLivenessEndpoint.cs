
namespace TemplateApp.Features.Health.GetLiveness;

internal static class GetLivenessEndpoint
{
    public static void Map(IEndpointRouteBuilder health) => health.MapGet("/live", Handle);

    private static IResult Handle(TimeProvider clock) =>
        Results.Ok(new HealthStatusResponse("live", clock.GetUtcNow()));
}
