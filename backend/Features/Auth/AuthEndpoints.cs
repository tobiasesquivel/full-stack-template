using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/auth");
        group.AddEndpointFilter<SessionHttpPolicy>();

        group.MapGet("/csrf", IssueCsrf).AllowAnonymous();

        group.MapPost("/register", RegisterAsync).AllowAnonymous().RequireRateLimiting("auth");
        group.MapPost("/login", LoginAsync).AllowAnonymous().RequireRateLimiting("auth");
        group.MapPost("/refresh", RefreshAsync).AllowAnonymous().RequireRateLimiting("auth");
        group.MapPost("/logout", LogoutAsync).AllowAnonymous().RequireRateLimiting("auth");

        return app;
    }

    private static IResult IssueCsrf(HttpContext http, SessionHttpPolicy policy)
    {
        policy.IssueCsrf(http);
        return Results.Ok();
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        HttpContext http,
        AuthModule module,
        SessionHttpPolicy policy,
        CancellationToken ct)
    {
        var result = await module.RegisterAsync(request, ct);
        if (result.IsError)
            return ApiResults.ToResult(result, _ => Results.Created());

        (AuthResponse response, string refresh) = result.Value;
        policy.AttachRefresh(http, refresh);
        return Results.Created($"/api/v1/users/{response.User.AuthId}", response);
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext http,
        AuthModule module,
        SessionHttpPolicy policy,
        CancellationToken ct)
    {
        var result = await module.LoginAsync(request, ct);
        if (result.IsError)
        {
            if (result.Errors.Any(e => e.Type == ErrorOr.ErrorType.Unauthorized))
                policy.ClearRefresh(http);
            return ApiResults.ToResult(result, _ => Results.Ok());
        }

        (AuthResponse response, string refresh) = result.Value;
        policy.AttachRefresh(http, refresh);
        return Results.Ok(response);
    }

    private static async Task<IResult> RefreshAsync(
        HttpContext http,
        AuthModule module,
        SessionHttpPolicy policy,
        CancellationToken ct)
    {
        if (!policy.TryGetRefresh(http.Request, out string? plaintext) || string.IsNullOrEmpty(plaintext))
        {
            policy.ClearRefresh(http);
            return ApiResults.ToResult<TokenResponse>(AuthErrors.InvalidSession, _ => Results.Ok());
        }

        var result = await module.RefreshAsync(plaintext, ct);
        if (result.IsError)
        {
            policy.ClearRefresh(http);
            return ApiResults.ToResult(result, _ => Results.Ok());
        }

        (TokenResponse response, string next) = result.Value;
        policy.AttachRefresh(http, next);
        return Results.Ok(response);
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext http,
        AuthModule module,
        SessionHttpPolicy policy,
        CancellationToken ct)
    {
        policy.TryGetRefresh(http.Request, out string? plaintext);
        await module.LogoutAsync(plaintext, ct);
        policy.ClearRefresh(http);
        return Results.NoContent();
    }
}
