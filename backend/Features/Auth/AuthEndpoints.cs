using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/auth");

        group.MapGet("/csrf", IssueCsrfAsync).AllowAnonymous();

        group.MapPost("/register", RegisterAsync).AllowAnonymous().RequireRateLimiting("auth");
        group.MapPost("/login", LoginAsync).AllowAnonymous().RequireRateLimiting("auth");
        group.MapPost("/refresh", RefreshAsync).AllowAnonymous().RequireRateLimiting("auth");
        group.MapPost("/logout", LogoutAsync).AllowAnonymous().RequireRateLimiting("auth");

        return app;
    }

    private static Task<IResult> IssueCsrfAsync(HttpContext http, IAntiforgery antiforgery, IHostEnvironment environment)
    {
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(http);

        http.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken ?? string.Empty, new CookieOptions
        {
            HttpOnly = false,
            Secure = !environment.IsDevelopment(),
            SameSite = SameSiteMode.Lax,
            Path = "/",
        });

        return Task.FromResult<IResult>(Results.Ok());
    }

    private static async Task<IResult?> ValidateAntiforgeryAsync(HttpContext http)
    {
        IAntiforgery antiforgery = http.RequestServices.GetRequiredService<IAntiforgery>();

        try
        {
            await antiforgery.ValidateRequestAsync(http);
            return null;
        }
        catch (AntiforgeryValidationException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid antiforgery token.",
                extensions: new Dictionary<string, object?> { ["code"] = "Auth.AntiforgeryFailed" });
        }
    }

    private static CookieOptions RefreshCookieOptions(IHostEnvironment environment) => new()
    {
        HttpOnly = true,
        Secure = !environment.IsDevelopment(),
        SameSite = SameSiteMode.Strict,
        Path = "/api/v1/auth",
    };

    private static void AppendRefreshCookie(HttpContext http, string plaintext, IHostEnvironment environment) =>
        http.Response.Cookies.Append("template_refresh", plaintext, RefreshCookieOptions(environment));

    private static void ClearRefreshCookie(HttpContext http, IHostEnvironment environment)
    {
        CookieOptions options = RefreshCookieOptions(environment);
        options.MaxAge = TimeSpan.Zero;
        http.Response.Cookies.Append("template_refresh", string.Empty, options);
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        HttpContext http,
        AuthModule module,
        IHostEnvironment environment,
        CancellationToken ct)
    {
        IResult? antiforgery = await ValidateAntiforgeryAsync(http);
        if (antiforgery is not null)
            return antiforgery;

        var result = await module.RegisterAsync(request, ct);
        if (result.IsError)
            return ApiResults.ToResult(result, _ => Results.Created());

        (AuthResponse response, string refresh) = result.Value;
        AppendRefreshCookie(http, refresh, environment);
        return Results.Created($"/api/v1/users/{response.User.AuthId}", response);
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext http,
        AuthModule module,
        IHostEnvironment environment,
        CancellationToken ct)
    {
        IResult? antiforgery = await ValidateAntiforgeryAsync(http);
        if (antiforgery is not null)
            return antiforgery;

        var result = await module.LoginAsync(request, ct);
        if (result.IsError)
        {
            if (result.Errors.Any(e => e.Type == ErrorOr.ErrorType.Unauthorized))
                ClearRefreshCookie(http, environment);
            return ApiResults.ToResult(result, _ => Results.Ok());
        }

        (AuthResponse response, string refresh) = result.Value;
        AppendRefreshCookie(http, refresh, environment);
        return Results.Ok(response);
    }

    private static async Task<IResult> RefreshAsync(
        HttpContext http,
        AuthModule module,
        TokenService tokens,
        IHostEnvironment environment,
        CancellationToken ct)
    {
        IResult? antiforgery = await ValidateAntiforgeryAsync(http);
        if (antiforgery is not null)
            return antiforgery;

        if (!TokenService.TryGetRefreshPlaintext(http.Request, out string? plaintext) || string.IsNullOrEmpty(plaintext))
        {
            ClearRefreshCookie(http, environment);
            return ApiResults.ToResult<TokenResponse>(AuthErrors.InvalidSession, _ => Results.Ok());
        }

        var result = await module.RefreshAsync(plaintext, ct);
        if (result.IsError)
        {
            ClearRefreshCookie(http, environment);
            return ApiResults.ToResult(result, _ => Results.Ok());
        }

        (TokenResponse response, string next) = result.Value;
        AppendRefreshCookie(http, next, environment);
        return Results.Ok(response);
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext http,
        AuthModule module,
        IHostEnvironment environment,
        CancellationToken ct)
    {
        IResult? antiforgery = await ValidateAntiforgeryAsync(http);
        if (antiforgery is not null)
            return antiforgery;

        TokenService.TryGetRefreshPlaintext(http.Request, out string? plaintext);
        await module.LogoutAsync(plaintext, ct);
        ClearRefreshCookie(http, environment);
        return Results.NoContent();
    }
}
