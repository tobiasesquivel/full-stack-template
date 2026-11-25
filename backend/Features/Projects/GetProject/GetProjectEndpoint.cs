using System.Security.Claims;
using ErrorOr;
using TemplateApp.Shared.Http;
using Wolverine;

namespace TemplateApp.Features.Projects.GetProject;

internal static class GetProjectEndpoint
{
    public static void Map(IEndpointRouteBuilder projects) => projects.MapGet("/{id:guid}", HandleAsync).WithName(ProjectEndpointNames.GetProject);

    private static async Task<IResult> HandleAsync(
        Guid id,
        IMessageBus bus,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var command = new GetProjectQuery(user.GetCallerId(), id);
        var result = await bus.InvokeAsync<ErrorOr<ProjectResponse>>(command, ct);
        return ApiResults.ToResult(result, value => Results.Ok(value));
    }
}
