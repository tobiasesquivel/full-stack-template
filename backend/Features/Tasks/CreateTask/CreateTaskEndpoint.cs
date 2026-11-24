using System.Security.Claims;
using ErrorOr;
using TemplateApp.Features.Tasks.Access;
using TemplateApp.Infrastructure.Http.Idempotency;
using TemplateApp.Shared.Http;
using Wolverine;

namespace TemplateApp.Features.Tasks.CreateTask;

internal static class CreateTaskEndpoint
{
    public static void Map(IEndpointRouteBuilder tasks) => tasks.MapPost("", HandleAsync).WithIdempotency<CreateTaskRequest>();

    private static async Task<IResult> HandleAsync(
        CreateTaskRequest request,
        IMessageBus bus,
        TaskAccess access,
        ClaimsPrincipal user,
        HttpContext http,
        LinkGenerator links,
        CancellationToken ct)
    {
        var command = new CreateTaskCommand(access.CallerOf(user), request.Title, request.ProjectId);
        var result = await bus.InvokeAsync<ErrorOr<TaskResponse>>(command, ct);
        return ApiResults.ToResult(result, value => Results.Created(links.GetPathByName(http, TaskEndpointNames.GetTask, new { id = value.Id }), value));
    }
}
