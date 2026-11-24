using System.Security.Claims;

namespace TemplateApp.Infrastructure.Http.Remote;

public static class OnBehalfOf
{
    public const string Header = "X-On-Behalf-Of";

    private const string ItemKey = "TemplateApp.OnBehalfOf";

    public static string? UserOf(HttpContext http) =>
        http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? http.Items[ItemKey] as string;

    internal static void Accept(HttpContext http)
    {
        string? userId = http.Request.Headers[Header];
        if (!string.IsNullOrWhiteSpace(userId))
            http.Items[ItemKey] = userId;
    }

    internal sealed class ForwardingHandler(IHttpContextAccessor accessor) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? userId = accessor.HttpContext is { } http ? UserOf(http) : null;
            if (userId is not null)
                request.Headers.TryAddWithoutValidation(Header, userId);

            return base.SendAsync(request, cancellationToken);
        }
    }
}
