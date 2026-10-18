using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using TemplateApp.Features.Auth.Domain;

namespace TemplateApp.Features.Auth;

public sealed class SessionHttpPolicy
{
    public const string Scheme = CookieAuthenticationDefaults.AuthenticationScheme;
    public const string CookieName = "__Host-session";
    private const string XsrfCookieName = "XSRF-TOKEN";

    private readonly IAntiforgery _antiforgery;

    public SessionHttpPolicy(IAntiforgery antiforgery)
    {
        _antiforgery = antiforgery;
    }

    public static void ConfigureCookie(CookieAuthenticationOptions options, SessionOptions session)
    {
        options.Cookie.Name = CookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.Path = "/";
        options.ExpireTimeSpan = session.Lifetime;
        options.SlidingExpiration = false;
        options.Events.OnValidatePrincipal = ValidatePrincipalAsync;
        options.Events.OnRedirectToLogin = context => Respond(context.Response, StatusCodes.Status401Unauthorized);
        options.Events.OnRedirectToAccessDenied = context => Respond(context.Response, StatusCodes.Status403Forbidden);
    }

    public static async Task RequireAntiforgeryAsync(HttpContext http, RequestDelegate next)
    {
        if (!IsUnsafe(http.Request) || !http.Request.Path.StartsWithSegments("/api", StringComparison.Ordinal))
        {
            await next(http);
            return;
        }

        IAntiforgery antiforgery = http.RequestServices.GetRequiredService<IAntiforgery>();
        if (await antiforgery.IsRequestValidAsync(http))
        {
            await next(http);
            return;
        }

        await Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid antiforgery token.",
                extensions: new Dictionary<string, object?> { ["code"] = "Auth.AntiforgeryFailed" })
            .ExecuteAsync(http);
    }

    public async Task SignInAsync(HttpContext http, Session session)
    {
        ClaimsPrincipal principal = SessionPrincipal.Create(session, Scheme);
        await http.SignInAsync(Scheme, principal, new AuthenticationProperties
        {
            IsPersistent = false,
            AllowRefresh = false,
            IssuedUtc = session.CreatedAt,
            ExpiresUtc = session.ExpiresAt,
        });
        http.User = principal;
        IssueCsrf(http);
    }

    public async Task SignOutAsync(HttpContext http)
    {
        await http.SignOutAsync(Scheme);
        http.User = new ClaimsPrincipal(new ClaimsIdentity());
        IssueCsrf(http);
    }

    public void IssueCsrf(HttpContext http)
    {
        AntiforgeryTokenSet tokens = _antiforgery.GetAndStoreTokens(http);

        http.Response.Cookies.Append(XsrfCookieName, tokens.RequestToken ?? string.Empty, new CookieOptions
        {
            HttpOnly = false,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
        });
    }

    private static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        Sessions sessions = context.HttpContext.RequestServices.GetRequiredService<Sessions>();
        bool active = SessionPrincipal.TryRead(context.Principal, out string userId, out Guid sessionId)
            && await sessions.IsActiveAsync(sessionId, userId, context.HttpContext.RequestAborted);
        if (active)
            return;

        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(Scheme);
    }

    private static bool IsUnsafe(HttpRequest request) =>
        !(HttpMethods.IsGet(request.Method)
            || HttpMethods.IsHead(request.Method)
            || HttpMethods.IsOptions(request.Method)
            || HttpMethods.IsTrace(request.Method));

    private static Task Respond(HttpResponse response, int status)
    {
        response.StatusCode = status;
        return Task.CompletedTask;
    }
}
