using System.Security.Claims;
using TemplateApp.Infrastructure.Http;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Tasks.CreateTask;

internal static class CreateTaskEndpoint
{
    public static void Map(IEndpointRouteBuilder tasks) => tasks.MapPost("", HandleAsync).WithIdempotency<CreateTaskRequest>();

    private static async Task<IResult> HandleAsync(
        CreateTaskRequest request,
        CreateTask slice,
        ClaimsPrincipal user,
        HttpContext http,
        LinkGenerator links,
        CancellationToken ct)
    {
        var result = await slice.ExecuteAsync(user.GetCallerId(), request, ct);
        return ApiResults.ToResult(result, value => Results.Created(links.GetPathByName(http, TaskEndpointNames.GetTask, new { id = value.Id }), value));
    }
}
