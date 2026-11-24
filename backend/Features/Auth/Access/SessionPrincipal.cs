using System.Security.Claims;
using TemplateApp.Features.Auth.Domain;
using TemplateApp.Infrastructure.Http;

namespace TemplateApp.Features.Auth.Access;

internal static class SessionPrincipal
{
    public static ClaimsPrincipal Create(Session session, string scheme) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, session.UserId),
                new Claim(SessionCookie.SessionIdClaim, session.Id.ToString("D")),
            ],
            scheme));
}
