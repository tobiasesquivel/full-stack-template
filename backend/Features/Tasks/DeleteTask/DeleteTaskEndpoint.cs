using System.Security.Claims;
using ErrorOr;
using TemplateApp.Features.Tasks.Access;
using TemplateApp.Shared.Http;
using Wolverine;

namespace TemplateApp.Features.Tasks.DeleteTask;

internal static class DeleteTaskEndpoint
{
    public static void Map(IEndpointRouteBuilder tasks) => tasks.MapDelete("/{id:guid}", HandleAsync);

    private static async Task<IResult> HandleAsync(
        Guid id,
        IMessageBus bus,
        TaskAccess access,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var command = new DeleteTaskCommand(access.CallerOf(user), id);
        ErrorOr<Success> result = await bus.InvokeAsync<ErrorOr<Success>>(command, ct);
        return ApiResults.ToResult(result, _ => Results.NoContent());
    }
}
