using System.Security.Claims;
using ErrorOr;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Users.GetAccountClosure;

internal static class GetAccountClosureEndpoint
{
    public static void Map(IEndpointRouteBuilder users) =>
        users.MapGet("/closures/{closureId:guid}", HandleAsync);

    private static async Task<IResult> HandleAsync(Guid closureId, ClaimsPrincipal user, GetAccountClosure slice, CancellationToken ct)
    {
        ErrorOr<AccountClosureStatusResponse> result = await slice.ExecuteAsync(user.GetCallerId(), closureId, ct);
        return ApiResults.ToResult(result, value => Results.Ok(value));
    }
}
