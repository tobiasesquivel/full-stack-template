using ErrorOr;
using Microsoft.AspNetCore.DataProtection;
using TemplateApp.Infrastructure.Access;
using TemplateApp.Shared.Http;
using Wolverine;

namespace TemplateApp.Features.Auth.DeclarePermissions;

internal static class DeclarePermissionsEndpoint
{
    public static void Map(IEndpointRouteBuilder internalApi) => internalApi.MapPut("/feature-permissions/{feature}", HandleAsync);

    private static async Task<IResult> HandleAsync(string feature, DeclarePermissionsRequest request, IDataProtectionProvider keys, IMessageBus bus, CancellationToken ct)
    {
        FeaturePermissions? declaration = PermissionDeclarationSeal.Open(keys, request.Seal);
        if (declaration is null || !string.Equals(declaration.Feature, feature, StringComparison.Ordinal))
            return ApiResults.Problem(AuthErrors.DeclarationNotTrusted);

        var command = new DeclarePermissionsCommand(declaration.Feature, declaration.Grants);
        var result = await bus.InvokeAsync<ErrorOr<Success>>(command, ct);
        return ApiResults.ToResult(result, _ => Results.NoContent());
    }
}
