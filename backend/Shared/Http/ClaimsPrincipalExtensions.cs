using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace TemplateApp.Shared.Http;

public static class ClaimsPrincipalExtensions
{
    public static string GetCallerId(this ClaimsPrincipal user)
    {
        string? sub = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(sub))
            throw new InvalidOperationException("JWT is missing the 'sub' caller claim. Ensure MapInboundClaims=false.");

        return sub;
    }
}
