namespace TemplateApp.Features.Tasks.SnapshotTaskCounts;

internal static class SnapshotTaskCountsEndpoint
{
    public static void Map(IEndpointRouteBuilder internalApi) => internalApi.MapGet("/task-count-snapshot", HandleAsync);

    private static async Task<IResult> HandleAsync(SnapshotTaskCounts slice, CancellationToken ct) =>
        Results.Ok(await slice.ExecuteAsync(ct));
}
