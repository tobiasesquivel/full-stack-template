using ErrorOr;
using TemplateApp.Shared.Http;
using Wolverine;

namespace TemplateApp.Features.Projects.FindAssignableProject;

internal static class FindAssignableProjectEndpoint
{
    public static void Map(IEndpointRouteBuilder internalApi) =>
        internalApi.MapGet("/owners/{ownerId}/projects/{projectId:guid}", HandleAsync);

    private static async Task<IResult> HandleAsync(string ownerId, Guid projectId, IMessageBus bus, CancellationToken ct)
    {
        var command = new FindAssignableProjectCommand(ownerId, projectId);
        var result = await bus.InvokeAsync<ErrorOr<ProjectAssignmentResponse>>(command, ct);
        return ApiResults.ToResult(result, Results.Ok);
    }
}
