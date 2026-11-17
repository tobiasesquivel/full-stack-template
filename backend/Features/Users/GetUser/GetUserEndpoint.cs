using System.Security.Claims;
using ErrorOr;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Users.GetUser;

internal static class GetUserEndpoint
{
    public static void Map(IEndpointRouteBuilder users)
    {
        users.MapGet("/me", HandleMeAsync);
        users.MapGet("/{authId}", HandleByIdAsync).WithName(UserEndpointNames.GetUser);
    }

    private static Task<IResult> HandleMeAsync(ClaimsPrincipal user, GetUser slice, CancellationToken ct) =>
        HandleAsync(user.GetCallerId(), user, slice, ct);

    private static Task<IResult> HandleByIdAsync(string authId, ClaimsPrincipal user, GetUser slice, CancellationToken ct) =>
        HandleAsync(authId, user, slice, ct);

    private static async Task<IResult> HandleAsync(string authId, ClaimsPrincipal user, GetUser slice, CancellationToken ct)
    {
        ErrorOr<UserResponse> result = await slice.ExecuteAsync(user.GetCallerId(), authId, ct);
        return ApiResults.ToResult(result, value => Results.Ok(value));
    }
}
