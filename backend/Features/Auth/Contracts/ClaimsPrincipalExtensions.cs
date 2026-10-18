using System.Security.Claims;

namespace TemplateApp.Features.Auth.Contracts;

public static class ClaimsPrincipalExtensions
{
    public static string GetCallerId(this ClaimsPrincipal user)
    {
        string? id = user.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException("The session is missing the caller id claim.");

        return id;
    }
}
