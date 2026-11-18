using System.Security.Claims;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Tasks.GetTask;

internal static class GetTaskEndpoint
{
    public static void Map(IEndpointRouteBuilder tasks) => tasks.MapGet("/{id:guid}", HandleAsync).WithName(TaskEndpointNames.GetTask);

    private static async Task<IResult> HandleAsync(
        Guid id,
        GetTask slice,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var result = await slice.ExecuteAsync(user.GetCallerId(), id, ct);
        return ApiResults.ToResult(result, value => Results.Ok(value));
    }
}
