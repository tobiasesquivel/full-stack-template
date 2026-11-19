using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Projects.ApplyTaskEvent;

internal static class ApplyTaskEventEndpoint
{
    public static void Map(IEndpointRouteBuilder internalApi) =>
        internalApi.MapPut("/task-events/{messageId:guid}", HandleAsync);

    private static async Task<IResult> HandleAsync(Guid messageId, TaskEventRequest request, ApplyTaskEvent slice, CancellationToken ct) =>
        ApiResults.ToResult(await slice.ExecuteAsync(messageId, request, ct), _ => Results.NoContent());
}
