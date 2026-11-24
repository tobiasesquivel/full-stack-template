using ErrorOr;
using TemplateApp.Shared.Http;
using Wolverine;

namespace TemplateApp.Features.Tasks.SnapshotTaskCounts;

internal static class SnapshotTaskCountsEndpoint
{
    public static void Map(IEndpointRouteBuilder internalApi) => internalApi.MapGet("/task-count-snapshot", HandleAsync);

    private static async Task<IResult> HandleAsync(IMessageBus bus, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<ErrorOr<TaskCountsSnapshotResponse>>(new SnapshotTaskCountsCommand(), ct);
        return ApiResults.ToResult(result, value => Results.Ok(value));
    }
}
