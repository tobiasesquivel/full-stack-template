using System.Security.Claims;
using ErrorOr;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Users.UpdateProfile;

internal static class UpdateProfileEndpoint
{
    public static void Map(IEndpointRouteBuilder users) => users.MapPut("/me", HandleAsync);

    private static async Task<IResult> HandleAsync(
        UpdateProfileRequest request,
        ClaimsPrincipal user,
        UpdateProfile slice,
        CancellationToken ct)
    {
        ErrorOr<UserResponse> result = await slice.ExecuteAsync(user.GetCallerId(), request, ct);
        return ApiResults.ToResult(result, value => Results.Ok(value));
    }
}
