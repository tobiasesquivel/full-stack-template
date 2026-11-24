using System.Security.Claims;
using ErrorOr;
using TemplateApp.Infrastructure.Http;
using TemplateApp.Shared.Http;
using Wolverine;

namespace TemplateApp.Features.Projects.CreateProject;

internal static class CreateProjectEndpoint
{
    public static void Map(IEndpointRouteBuilder projects) => projects.MapPost("", HandleAsync).WithIdempotency<CreateProjectRequest>();

    private static async Task<IResult> HandleAsync(
        CreateProjectRequest request,
        IMessageBus bus,
        ClaimsPrincipal user,
        HttpContext http,
        LinkGenerator links,
        CancellationToken ct)
    {
        var command = new CreateProjectCommand(user.GetCallerId(), request.Name);
        var result = await bus.InvokeAsync<ErrorOr<ProjectResponse>>(command, ct);
        return ApiResults.ToResult(result, value => Results.Created(links.GetPathByName(http, ProjectEndpointNames.GetProject, new { id = value.Id }), value));
    }
}
