using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using TemplateApp.Features.Auth.Domain;
using TemplateApp.Infrastructure.Http;

namespace TemplateApp.Features.Auth.Access;

internal sealed class SessionHttpPolicy
{
    private const string XsrfCookieName = "XSRF-TOKEN";

    private readonly IAntiforgery _antiforgery;

    public SessionHttpPolicy(IAntiforgery antiforgery)
    {
        _antiforgery = antiforgery;
    }

    public static void ConfigureCookie(CookieAuthenticationOptions options, SessionOptions session)
    {
        options.ExpireTimeSpan = session.Lifetime;
        options.Events.OnValidatePrincipal = ValidatePrincipalAsync;
    }

    public async Task SignInAsync(HttpContext http, Session session)
    {
        ClaimsPrincipal principal = SessionPrincipal.Create(session, SessionCookie.Scheme);
        await http.SignInAsync(SessionCookie.Scheme, principal, new AuthenticationProperties
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
        await http.SignOutAsync(SessionCookie.Scheme);
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
        bool active = SessionCookie.TryRead(context.Principal, out string userId, out Guid sessionId)
            && await sessions.IsActiveAsync(sessionId, userId, context.HttpContext.RequestAborted);
        if (active)
            return;

        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(SessionCookie.Scheme);
    }
}
