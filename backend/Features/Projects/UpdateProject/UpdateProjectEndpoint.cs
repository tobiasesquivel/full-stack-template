using System.Security.Claims;
using ErrorOr;
using TemplateApp.Shared.Http;
using Wolverine;

namespace TemplateApp.Features.Projects.UpdateProject;

internal static class UpdateProjectEndpoint
{
    public static void Map(IEndpointRouteBuilder projects) => projects.MapPut("/{id:guid}", HandleAsync);

    private static async Task<IResult> HandleAsync(
        Guid id,
        UpdateProjectRequest request,
        IMessageBus bus,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var command = new UpdateProjectCommand(user.GetCallerId(), id, request.Name, request.IsArchived, request.Version);
        var result = await bus.InvokeAsync<ErrorOr<ProjectResponse>>(command, ct);
        return ApiResults.ToResult(result, value => Results.Ok(value));
    }
}
