using Microsoft.AspNetCore.Antiforgery;

namespace TemplateApp.Features.Auth;

public sealed class SessionHttpPolicy : IEndpointFilter
{
    private const string RefreshCookieName = "template_refresh";
    private const string XsrfCookieName = "XSRF-TOKEN";

    private readonly IAntiforgery _antiforgery;
    private readonly IHostEnvironment _environment;

    public SessionHttpPolicy(IAntiforgery antiforgery, IHostEnvironment environment)
    {
        _antiforgery = antiforgery;
        _environment = environment;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!HttpMethods.IsPost(context.HttpContext.Request.Method))
            return await next(context);

        try
        {
            await _antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid antiforgery token.",
                extensions: new Dictionary<string, object?> { ["code"] = "Auth.AntiforgeryFailed" });
        }

        return await next(context);
    }

    public void IssueCsrf(HttpContext http)
    {
        AntiforgeryTokenSet tokens = _antiforgery.GetAndStoreTokens(http);

        http.Response.Cookies.Append(XsrfCookieName, tokens.RequestToken ?? string.Empty, new CookieOptions
        {
            HttpOnly = false,
            Secure = !_environment.IsDevelopment(),
            SameSite = SameSiteMode.Lax,
            Path = "/",
        });
    }

    public void AttachRefresh(HttpContext http, string plaintext) =>
        http.Response.Cookies.Append(RefreshCookieName, plaintext, RefreshCookieOptions());

    public void ClearRefresh(HttpContext http)
    {
        CookieOptions options = RefreshCookieOptions();
        options.MaxAge = TimeSpan.Zero;
        http.Response.Cookies.Append(RefreshCookieName, string.Empty, options);
    }

    public bool TryGetRefresh(HttpRequest request, out string? plaintext)
    {
        plaintext = null;
        if (!request.Cookies.TryGetValue(RefreshCookieName, out string? raw) || string.IsNullOrEmpty(raw))
            return false;

        plaintext = raw;
        return true;
    }

    private CookieOptions RefreshCookieOptions() => new()
    {
        HttpOnly = true,
        Secure = !_environment.IsDevelopment(),
        SameSite = SameSiteMode.Strict,
        Path = "/api/v1/auth",
    };
}
