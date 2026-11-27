using TemplateApp.Shared.Architecture;

namespace TemplateApp.Features.Health.GetLiveness;

[DuneExemption(DuneCheck.MediatorEndpoint, "La sonda de vida responde sin caso de uso ni base de datos.")]
internal static class GetLivenessEndpoint
{
    public static void Map(IEndpointRouteBuilder health) => health.MapGet("/live", Handle);

    private static IResult Handle(TimeProvider clock) =>
        Results.Ok(new HealthStatusResponse("live", clock.GetUtcNow()));
}
