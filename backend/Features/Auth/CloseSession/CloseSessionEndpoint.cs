using ErrorOr;
using TemplateApp.Features.Auth.Access;
using Wolverine;
using TemplateApp.Infrastructure.Http;
using TemplateApp.Shared.Architecture;

namespace TemplateApp.Features.Auth.CloseSession;

[DuneExemption(DuneCheck.IdempotentPost, "Cerrar una sesión ya cerrada no cambia nada.")]
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
