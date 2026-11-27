using ErrorOr;
using TemplateApp.Shared.Architecture;
using TemplateApp.Shared.Http;
using Wolverine;

namespace TemplateApp.Features.Auth.CheckSession;

[DuneExemption(DuneCheck.IdempotentPost, "El gateway consulta la sesión en cada request; no crea un recurso.")]
internal static class CheckSessionEndpoint
{
    public static void Map(IEndpointRouteBuilder internalApi) => internalApi.MapPost("/session-checks", HandleAsync);

    private static async Task<IResult> HandleAsync(CheckSessionRequest request, IMessageBus bus, CancellationToken ct)
    {
        var command = new CheckSessionCommand(request.UserId, request.SessionId);
        var result = await bus.InvokeAsync<ErrorOr<CheckSessionResponse>>(command, ct);
        return ApiResults.ToResult(result, Results.Ok);
    }
}
