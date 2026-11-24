using ErrorOr;
using Wolverine;
using TemplateApp.Infrastructure.Http;

namespace TemplateApp.Features.Auth.CloseSession;

internal static class CloseSessionEndpoint
{
    public static void Map(IEndpointRouteBuilder auth) =>
        auth.MapPost("/logout", HandleAsync).AllowAnonymous().RequireRateLimiting(AuthFeature.RateLimitPolicy);

    private static async Task<IResult> HandleAsync(
        HttpContext http,
        IMessageBus bus,
        SessionHttpPolicy policy,
        CancellationToken ct)
    {
        var command = new CloseSessionCommand(SessionCookie.TryRead(http.User, out _, out Guid sessionId) ? sessionId : null);
        await bus.InvokeAsync<ErrorOr<Success>>(command, ct);
        await policy.SignOutAsync(http);
        return Results.NoContent();
    }
}
