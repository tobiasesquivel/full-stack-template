using System.Security.Claims;
using ErrorOr;
using TemplateApp.Shared.Http;
using Wolverine;

namespace TemplateApp.Features.Users.GetAccountClosureHistory;

internal static class GetAccountClosureHistoryEndpoint
{
    public static void Map(IEndpointRouteBuilder users) =>
        users.MapGet("/closures/{closureId:guid}/history", HandleAsync);

    private static async Task<IResult> HandleAsync(Guid closureId, ClaimsPrincipal user, IMessageBus bus, CancellationToken ct)
    {
        var command = new GetAccountClosureHistoryQuery(user.GetCallerId(), closureId);
        var result = await bus.InvokeAsync<ErrorOr<AccountClosureHistoryResponse>>(command, ct);
        return ApiResults.ToResult(result, value => Results.Ok(value));
    }
}
