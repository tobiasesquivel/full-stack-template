using System.Security.Claims;
using ErrorOr;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Shared.Http;
using Wolverine;

namespace TemplateApp.Features.Users.UpdateProfile;

internal static class UpdateProfileEndpoint
{
    public static void Map(IEndpointRouteBuilder users) => users.MapPut("/me", HandleAsync);

    private static async Task<IResult> HandleAsync(
        UpdateProfileRequest request,
        ClaimsPrincipal user,
        IMessageBus bus,
        CancellationToken ct)
    {
        var command = new UpdateProfileCommand(user.GetCallerId(), request.DisplayName, request.Timezone);
        var result = await bus.InvokeAsync<ErrorOr<UserResponse>>(command, ct);
        return ApiResults.ToResult(result, value => Results.Ok(value));
    }
}
