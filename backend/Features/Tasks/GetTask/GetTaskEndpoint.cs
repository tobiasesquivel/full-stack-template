using System.Security.Claims;
using ErrorOr;
using TemplateApp.Features.Tasks.Access;
using TemplateApp.Shared.Http;
using Wolverine;

namespace TemplateApp.Features.Tasks.GetTask;

internal static class GetTaskEndpoint
{
    public static void Map(IEndpointRouteBuilder tasks) => tasks.MapGet("/{id:guid}", HandleAsync).WithName(TaskEndpointNames.GetTask);

    private static async Task<IResult> HandleAsync(
        Guid id,
        IMessageBus bus,
        TaskAccess access,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var command = new GetTaskQuery(access.CallerOf(user), id);
        var result = await bus.InvokeAsync<ErrorOr<TaskResponse>>(command, ct);
        return ApiResults.ToResult(result, value => Results.Ok(value));
    }
}
