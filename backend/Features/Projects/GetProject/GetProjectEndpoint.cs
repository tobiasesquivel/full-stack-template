using System.Security.Claims;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Projects.GetProject;

internal static class GetProjectEndpoint
{
    public static void Map(IEndpointRouteBuilder projects) => projects.MapGet("/{id:guid}", HandleAsync).WithName(ProjectEndpointNames.GetProject);

    private static async Task<IResult> HandleAsync(
        Guid id,
        GetProject slice,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var result = await slice.ExecuteAsync(user.GetCallerId(), id, ct);
        return ApiResults.ToResult(result, value => Results.Ok(value));
    }
}
