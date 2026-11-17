using System.Security.Claims;
using ErrorOr;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Users.DeleteUser;

internal static class DeleteUserEndpoint
{
    public static void Map(IEndpointRouteBuilder users)
    {
        users.MapDelete("/me", HandleMeAsync);
        users.MapDelete("/{authId}", HandleByIdAsync);
    }

    private static Task<IResult> HandleMeAsync(ClaimsPrincipal user, DeleteUser slice, CancellationToken ct) =>
        HandleAsync(user.GetCallerId(), user, slice, ct);

    private static Task<IResult> HandleByIdAsync(string authId, ClaimsPrincipal user, DeleteUser slice, CancellationToken ct) =>
        HandleAsync(authId, user, slice, ct);

    private static async Task<IResult> HandleAsync(string authId, ClaimsPrincipal user, DeleteUser slice, CancellationToken ct)
    {
        ErrorOr<Success> result = await slice.ExecuteAsync(user.GetCallerId(), authId, ct);
        return ApiResults.ToResult(result, _ => Results.NoContent());
    }
}
