using System.Security.Claims;
using ErrorOr;
using TemplateApp.Features.Tasks.Access;
using TemplateApp.Shared.Http;
using Wolverine;

namespace TemplateApp.Features.Tasks.ListTasks;

internal static class ListTasksEndpoint
{
    public static void Map(IEndpointRouteBuilder tasks) => tasks.MapGet("", HandleAsync);

    private static async Task<IResult> HandleAsync(
        IMessageBus bus,
        TaskAccess access,
        ClaimsPrincipal user,
        string? cursor = null,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var command = new ListTasksCommand(access.CallerOf(user), cursor, pageSize);
        var result = await bus.InvokeAsync<ErrorOr<TaskListResponse>>(command, ct);
        return ApiResults.ToResult(result, value => Results.Ok(value));
    }
}
