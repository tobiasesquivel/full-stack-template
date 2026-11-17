using System.Security.Claims;
using ErrorOr;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Tasks.DeleteTask;

internal static class DeleteTaskEndpoint
{
    public static void Map(IEndpointRouteBuilder tasks) => tasks.MapDelete("/{id:guid}", HandleAsync);

    private static async Task<IResult> HandleAsync(
        Guid id,
        DeleteTask slice,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        ErrorOr<Success> result = await slice.ExecuteAsync(user.GetCallerId(), id, ct);
        return ApiResults.ToResult(result, _ => Results.NoContent());
    }
}
