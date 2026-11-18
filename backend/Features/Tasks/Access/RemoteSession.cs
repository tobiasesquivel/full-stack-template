using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using TemplateApp.Infrastructure.Http;

namespace TemplateApp.Features.Tasks.Access;

internal static class RemoteSession
{
    public static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        HttpContext http = context.HttpContext;
        if (SessionCookie.TryRead(context.Principal, out string userId, out Guid sessionId))
        {
            AuthSessionClient auth = http.RequestServices.GetRequiredService<AuthSessionClient>();
            IReadOnlyList<string>? permissions = await auth.CheckAsync(userId, sessionId, http.RequestAborted);
            if (permissions is not null)
            {
                http.RequestServices.GetRequiredService<TaskAccess>().Grant(permissions);
                return;
            }
        }

        context.RejectPrincipal();
        await http.SignOutAsync(SessionCookie.Scheme);
    }
}
