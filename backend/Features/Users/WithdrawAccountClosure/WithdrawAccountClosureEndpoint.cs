using System.Security.Claims;
using ErrorOr;
using TemplateApp.Infrastructure.Http;
using TemplateApp.Shared.Http;
using Wolverine;

namespace TemplateApp.Features.Users.WithdrawAccountClosure;

internal static class WithdrawAccountClosureEndpoint
{
    public static void Map(IEndpointRouteBuilder users) =>
        users.MapPost("/closures/{closureId:guid}/cancel", HandleAsync).WithIdempotency<WithdrawAccountClosureRequest>();

    private static async Task<IResult> HandleAsync([AsParameters] WithdrawAccountClosureRequest request, ClaimsPrincipal user, IMessageBus bus, CancellationToken ct)
    {
        var command = new WithdrawAccountClosureCommand(user.GetCallerId(), request.ClosureId);
        var result = await bus.InvokeAsync<ErrorOr<Success>>(command, ct);
        return ApiResults.ToResult(result, _ => Results.NoContent());
    }
}
