using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Projects.FindAssignableProject;

internal static class FindAssignableProjectEndpoint
{
    public static void Map(IEndpointRouteBuilder internalApi) =>
        internalApi.MapGet("/owners/{ownerId}/projects/{projectId:guid}", HandleAsync);

    private static async Task<IResult> HandleAsync(string ownerId, Guid projectId, FindAssignableProject slice, CancellationToken ct) =>
        ApiResults.ToResult(await slice.ExecuteAsync(ownerId, projectId, ct), Results.Ok);
}
