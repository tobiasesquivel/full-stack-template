using System.Security.Claims;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Infrastructure.Http;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Projects.CreateProject;

internal static class CreateProjectEndpoint
{
    public static void Map(IEndpointRouteBuilder projects) => projects.MapPost("", HandleAsync).WithIdempotency<CreateProjectRequest>();

    private static async Task<IResult> HandleAsync(
        CreateProjectRequest request,
        CreateProject slice,
        ClaimsPrincipal user,
        HttpContext http,
        LinkGenerator links,
        CancellationToken ct)
    {
        var result = await slice.ExecuteAsync(user.GetCallerId(), request, ct);
        return ApiResults.ToResult(result, value => Results.Created(links.GetPathByName(http, ProjectEndpointNames.GetProject, new { id = value.Id }), value));
    }
}
