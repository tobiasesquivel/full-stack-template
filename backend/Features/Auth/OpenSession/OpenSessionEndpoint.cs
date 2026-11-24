using ErrorOr;
using TemplateApp.Features.Auth.Access;
using TemplateApp.Features.Auth.Domain;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Shared.Http;
using Wolverine;

namespace TemplateApp.Features.Auth.OpenSession;

internal static class OpenSessionEndpoint
{
    public static void Map(IEndpointRouteBuilder auth) =>
        auth.MapPost("/login", HandleAsync).AllowAnonymous().RequireRateLimiting(AuthFeature.RateLimitPolicy);

    private static async Task<IResult> HandleAsync(
        OpenSessionRequest request,
        HttpContext http,
        IMessageBus bus,
        SessionHttpPolicy policy,
        CancellationToken ct)
    {
        var command = new OpenSessionCommand(request.UserName, request.Password);
        var result = await bus.InvokeAsync<ErrorOr<(UserResponse User, Session Session)>>(command, ct);
        if (result.IsError)
            return ApiResults.ToResult(result, _ => Results.Ok());

        await policy.SignInAsync(http, result.Value.Session);
        return Results.Ok(result.Value.User);
    }
}
