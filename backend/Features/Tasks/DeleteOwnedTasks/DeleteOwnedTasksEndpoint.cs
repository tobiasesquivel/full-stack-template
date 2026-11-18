namespace TemplateApp.Features.Tasks.DeleteOwnedTasks;

internal static class DeleteOwnedTasksEndpoint
{
    public static void Map(IEndpointRouteBuilder internalApi) => internalApi.MapDelete("/owners/{ownerId}/tasks", HandleAsync);

    private static async Task<IResult> HandleAsync(string ownerId, DeleteOwnedTasks slice, CancellationToken ct)
    {
        await slice.ExecuteAsync(ownerId, ct);
        return Results.NoContent();
    }
}
