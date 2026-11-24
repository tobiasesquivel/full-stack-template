using System.Security.Claims;
using ErrorOr;
using TemplateApp.Shared.Http;
using Wolverine;

namespace TemplateApp.Features.Users.GetAccountClosure;

internal static class GetAccountClosureEndpoint
{
    public static void Map(IEndpointRouteBuilder users) =>
        users.MapGet("/closures/{closureId:guid}", HandleAsync);

    private static async Task<IResult> HandleAsync(Guid closureId, ClaimsPrincipal user, IMessageBus bus, CancellationToken ct)
    {
        var command = new GetAccountClosureCommand(user.GetCallerId(), closureId);
        var result = await bus.InvokeAsync<ErrorOr<AccountClosureStatusResponse>>(command, ct);
        return ApiResults.ToResult(result, value => Results.Ok(value));
    }
}
