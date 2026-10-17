using System.Security.Claims;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Tasks.CreateTask;

public static class CreateTaskEndpoint
{
    public static void Map(IEndpointRouteBuilder tasks) => tasks.MapPost("", HandleAsync);

    private static async Task<IResult> HandleAsync(
        CreateTaskRequest request,
        CreateTask slice,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var result = await slice.ExecuteAsync(user.GetCallerId(), request, ct);
        return ApiResults.ToResult(result, value => Results.Created($"/api/v1/tasks/{value.Id}", value));
    }
}
