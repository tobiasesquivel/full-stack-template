using ErrorOr;
using TemplateApp.Shared.Http;
using Wolverine;

namespace TemplateApp.Features.Auth.DeclarePermissions;

internal static class DeclarePermissionsEndpoint
{
    public static void Map(IEndpointRouteBuilder internalApi) => internalApi.MapPut("/feature-permissions/{feature}", HandleAsync);

    private static async Task<IResult> HandleAsync(string feature, DeclarePermissionsRequest request, IMessageBus bus, CancellationToken ct)
    {
        var command = new DeclarePermissionsCommand(feature, request.Grants);
        var result = await bus.InvokeAsync<ErrorOr<Success>>(command, ct);
        return ApiResults.ToResult(result, _ => Results.NoContent());
    }
}
