using System.Security.Claims;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Tasks.ListTasks;

internal static class ListTasksEndpoint
{
    public static void Map(IEndpointRouteBuilder tasks) => tasks.MapGet("", HandleAsync);

    private static async Task<IResult> HandleAsync(
        ListTasks slice,
        ClaimsPrincipal user,
        string? cursor = null,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await slice.ExecuteAsync(user.GetCallerId(), cursor, pageSize, ct);
        return ApiResults.ToResult(result, value => Results.Ok(value));
    }
}
