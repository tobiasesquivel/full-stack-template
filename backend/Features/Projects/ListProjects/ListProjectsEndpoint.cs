using System.Security.Claims;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Projects.ListProjects;

internal static class ListProjectsEndpoint
{
    public static void Map(IEndpointRouteBuilder projects) => projects.MapGet("", HandleAsync);

    private static async Task<IResult> HandleAsync(
        ListProjects slice,
        ClaimsPrincipal user,
        string? cursor = null,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await slice.ExecuteAsync(user.GetCallerId(), cursor, pageSize, ct);
        return ApiResults.ToResult(result, value => Results.Ok(value));
    }
}
