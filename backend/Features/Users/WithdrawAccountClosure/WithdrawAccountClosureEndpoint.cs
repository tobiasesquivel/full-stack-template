using System.Security.Claims;
using ErrorOr;
using TemplateApp.Infrastructure.Http;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Users.WithdrawAccountClosure;

internal static class WithdrawAccountClosureEndpoint
{
    public static void Map(IEndpointRouteBuilder users) =>
        users.MapPost("/closures/{closureId:guid}/cancel", HandleAsync).WithIdempotency<WithdrawAccountClosureRequest>();

    private static async Task<IResult> HandleAsync([AsParameters] WithdrawAccountClosureRequest request, ClaimsPrincipal user, WithdrawAccountClosure slice, CancellationToken ct)
    {
        ErrorOr<Success> result = await slice.ExecuteAsync(user.GetCallerId(), request.ClosureId, ct);
        return ApiResults.ToResult(result, _ => Results.NoContent());
    }
}
