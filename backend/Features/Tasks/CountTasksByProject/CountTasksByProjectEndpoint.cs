using Microsoft.AspNetCore.Mvc;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Tasks.CountTasksByProject;

internal static class CountTasksByProjectEndpoint
{
    public static void Map(IEndpointRouteBuilder internalApi) => internalApi.MapGet("/task-counts", HandleAsync);

    private static async Task<IResult> HandleAsync(
        [FromQuery(Name = "projectId")] Guid[] projectIds,
        CountTasksByProject slice,
        CancellationToken ct) =>
        ApiResults.ToResult(await slice.ExecuteAsync(projectIds, ct), Results.Ok);
}
