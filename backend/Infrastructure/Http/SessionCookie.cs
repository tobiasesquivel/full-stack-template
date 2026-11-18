using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using TemplateApp.Shared.Http;

namespace TemplateApp.Infrastructure.Http;

public static class SessionCookie
{
    public const string Scheme = CookieAuthenticationDefaults.AuthenticationScheme;
    public const string Name = "__Host-session";
    public const string SessionIdClaim = "sid";
    public const string AntiforgeryHeader = "X-XSRF-TOKEN";

    public static AuthenticationBuilder AddSessionCookie(this IServiceCollection services)
    {
        services.AddAntiforgery(options =>
        {
            options.HeaderName = AntiforgeryHeader;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });
        services.AddAuthorization();

        return services.AddAuthentication(Scheme).AddCookie(Scheme, Configure);
    }

    public static IApplicationBuilder UseSessionAntiforgery(this IApplicationBuilder app) =>
        app.Use(RequireAntiforgeryAsync);

    public static bool TryRead(ClaimsPrincipal? principal, out string userId, out Guid sessionId)
    {
        userId = principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        sessionId = Guid.Empty;
        return userId.Length > 0
            && Guid.TryParse(principal?.FindFirstValue(SessionIdClaim), out sessionId);
    }

    private static void Configure(CookieAuthenticationOptions options)
    {
        options.Cookie.Name = Name;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.Path = "/";
        options.SlidingExpiration = false;
        options.Events.OnRedirectToLogin = context => ApiResults.Problem(SessionErrors.SessionRequired).ExecuteAsync(context.HttpContext);
        options.Events.OnRedirectToAccessDenied = context => ApiResults.Problem(SessionErrors.AccessDenied).ExecuteAsync(context.HttpContext);
    }

    private static async Task RequireAntiforgeryAsync(HttpContext http, RequestDelegate next)
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

        await ApiResults.Problem(SessionErrors.AntiforgeryFailed).ExecuteAsync(http);
    }

    private static bool IsUnsafe(HttpRequest request) =>
        !(HttpMethods.IsGet(request.Method)
            || HttpMethods.IsHead(request.Method)
            || HttpMethods.IsOptions(request.Method)
            || HttpMethods.IsTrace(request.Method));
}
