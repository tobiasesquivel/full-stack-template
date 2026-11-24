using System.Security.Claims;
using ErrorOr;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Shared.Http;
using Wolverine;

namespace TemplateApp.Features.Users.DeleteUser;

internal static class DeleteUserEndpoint
{
    private const string Me = "me";

    public static void Map(IEndpointRouteBuilder users)
    {
        users.MapDelete("/me", HandleMeAsync);
        users.MapDelete("/{authId}", HandleByIdAsync);
    }

    private static Task<IResult> HandleMeAsync(ClaimsPrincipal user, IMessageBus bus, HttpContext http, LinkGenerator links, CancellationToken ct) =>
        HandleAsync(user.GetCallerId(), Me, user, bus, http, links, ct);

    private static Task<IResult> HandleByIdAsync(string authId, ClaimsPrincipal user, IMessageBus bus, HttpContext http, LinkGenerator links, CancellationToken ct) =>
        HandleAsync(authId, authId, user, bus, http, links, ct);

    private static async Task<IResult> HandleAsync(
        string authId,
        string monitor,
        ClaimsPrincipal user,
        IMessageBus bus,
        HttpContext http,
        LinkGenerator links,
        CancellationToken ct)
    {
        var command = new DeleteUserCommand(user.GetCallerId(), authId);
        var result = await bus.InvokeAsync<ErrorOr<AccountClosureResponse>>(command, ct);
        return ApiResults.ToResult(result, value => Results.Accepted(links.GetPathByName(http, UserEndpointNames.GetUser, new { authId = monitor }), value));
    }
}
