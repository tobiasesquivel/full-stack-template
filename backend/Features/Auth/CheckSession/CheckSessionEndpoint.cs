using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Auth.CheckSession;

internal static class CheckSessionEndpoint
{
    public static void Map(IEndpointRouteBuilder internalApi) => internalApi.MapPost("/session-checks", HandleAsync);

    private static async Task<IResult> HandleAsync(CheckSessionRequest request, CheckSession slice, CancellationToken ct) =>
        ApiResults.ToResult(await slice.ExecuteAsync(request, ct), Results.Ok);
}
