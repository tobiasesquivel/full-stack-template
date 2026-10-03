using Microsoft.AspNetCore.Authorization;
using TemplateApp.Domain.Authorization;

namespace TemplateApp.Infrastructure.Authorization;

public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User.HasClaim(c => c.Type == AppPermissions.ClaimType && c.Value == requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
