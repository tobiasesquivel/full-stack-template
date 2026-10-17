using System.Security.Claims;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Tasks.ListTasks;

public static class ListTasksEndpoint
{
    public static void Map(IEndpointRouteBuilder tasks) => tasks.MapGet("", HandleAsync);

    private static async Task<IResult> HandleAsync(
        ListTasks slice,
        ClaimsPrincipal user,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await slice.ExecuteAsync(user.GetCallerId(), page, pageSize, ct);
        return ApiResults.ToResult(result, value => Results.Ok(value));
    }
}
