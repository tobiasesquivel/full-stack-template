using System.Security.Claims;
using ErrorOr;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Users.GetAccountClosureHistory;

internal static class GetAccountClosureHistoryEndpoint
{
    public static void Map(IEndpointRouteBuilder users) =>
        users.MapGet("/closures/{closureId:guid}/history", HandleAsync);

    private static async Task<IResult> HandleAsync(Guid closureId, ClaimsPrincipal user, GetAccountClosureHistory slice, CancellationToken ct)
    {
        ErrorOr<AccountClosureHistoryResponse> result = await slice.ExecuteAsync(user.GetCallerId(), closureId, ct);
        return ApiResults.ToResult(result, value => Results.Ok(value));
    }
}
