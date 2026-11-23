using System.Security.Claims;
using ErrorOr;
using TemplateApp.Features.Tasks.Access;
using TemplateApp.Shared.Http;
using Wolverine;

namespace TemplateApp.Features.Tasks.UpdateTask;

internal static class UpdateTaskEndpoint
{
    public static void Map(IEndpointRouteBuilder tasks) => tasks.MapPut("/{id:guid}", HandleAsync);

    private static async Task<IResult> HandleAsync(
        Guid id,
        UpdateTaskRequest request,
        IMessageBus bus,
        TaskAccess access,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var command = new UpdateTaskCommand(access.CallerOf(user), id, request.Title, request.IsCompleted, request.Version);
        var result = await bus.InvokeAsync<ErrorOr<TaskResponse>>(command, ct);
        return ApiResults.ToResult(result, value => Results.Ok(value));
    }
}
