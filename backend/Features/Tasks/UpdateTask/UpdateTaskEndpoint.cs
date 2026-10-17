using System.Security.Claims;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Tasks.UpdateTask;

public static class UpdateTaskEndpoint
{
    public static void Map(IEndpointRouteBuilder tasks) => tasks.MapPut("/{id:guid}", HandleAsync);

    private static async Task<IResult> HandleAsync(
        Guid id,
        UpdateTaskRequest request,
        UpdateTask slice,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var result = await slice.ExecuteAsync(user.GetCallerId(), id, request, ct);
        return ApiResults.ToResult(result, value => Results.Ok(value));
    }
}
