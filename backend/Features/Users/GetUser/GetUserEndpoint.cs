using System.Security.Claims;
using ErrorOr;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Shared.Http;
using Wolverine;

namespace TemplateApp.Features.Users.GetUser;

internal static class GetUserEndpoint
{
    public static void Map(IEndpointRouteBuilder users)
    {
        users.MapGet("/me", HandleMeAsync);
        users.MapGet("/{authId}", HandleByIdAsync).WithName(UserEndpointNames.GetUser);
    }

    private static Task<IResult> HandleMeAsync(ClaimsPrincipal user, IMessageBus bus, CancellationToken ct) =>
        HandleAsync(user.GetCallerId(), user, bus, ct);

    private static Task<IResult> HandleByIdAsync(string authId, ClaimsPrincipal user, IMessageBus bus, CancellationToken ct) =>
        HandleAsync(authId, user, bus, ct);

    private static async Task<IResult> HandleAsync(string authId, ClaimsPrincipal user, IMessageBus bus, CancellationToken ct)
    {
        var command = new GetUserQuery(user.GetCallerId(), authId);
        var result = await bus.InvokeAsync<ErrorOr<UserResponse>>(command, ct);
        return ApiResults.ToResult(result, value => Results.Ok(value));
    }
}
