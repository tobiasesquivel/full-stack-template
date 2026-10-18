using System.Security.Claims;
using TemplateApp.Features.Auth.Domain;

namespace TemplateApp.Features.Auth;

public static class SessionPrincipal
{
    public const string SessionIdClaim = "sid";

    public static ClaimsPrincipal Create(Session session, string scheme) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, session.UserId),
                new Claim(SessionIdClaim, session.Id.ToString("D")),
            ],
            scheme));

    public static bool TryRead(ClaimsPrincipal? principal, out string userId, out Guid sessionId)
    {
        userId = principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        sessionId = Guid.Empty;
        return userId.Length > 0
            && Guid.TryParse(principal?.FindFirstValue(SessionIdClaim), out sessionId);
    }
}
