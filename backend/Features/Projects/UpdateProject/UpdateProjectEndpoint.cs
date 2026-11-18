using System.Security.Claims;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Projects.UpdateProject;

internal static class UpdateProjectEndpoint
{
    public static void Map(IEndpointRouteBuilder projects) => projects.MapPut("/{id:guid}", HandleAsync);

    private static async Task<IResult> HandleAsync(
        Guid id,
        UpdateProjectRequest request,
        UpdateProject slice,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var result = await slice.ExecuteAsync(user.GetCallerId(), id, request, ct);
        return ApiResults.ToResult(result, value => Results.Ok(value));
    }
}
