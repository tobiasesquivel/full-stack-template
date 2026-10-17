using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Auth.Access;

public sealed class RoleSeeder : IDatabaseSeeder
{
    private readonly RoleManager<IdentityRole> _roleManager;

    public RoleSeeder(RoleManager<IdentityRole> roleManager)
    {
        _roleManager = roleManager;
    }

    public async Task SeedAsync(CancellationToken ct)
    {
        foreach (string roleName in AppRoles.GetAll())
        {
            bool roleExists = await _roleManager.RoleExistsAsync(roleName);
            if (!roleExists)
            {
                IdentityResult createdResult = await _roleManager.CreateAsync(new IdentityRole(roleName));
                if (!createdResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Failed to seed role '{roleName}': {string.Join("; ", createdResult.Errors.Select(e => e.Description))}");
                }
            }

            IdentityRole? role = await _roleManager.FindByNameAsync(roleName);
            if (role is null)
            {
                continue;
            }

            await SeedRolePermissionsAsync(role, roleName);
        }
    }

    private async Task SeedRolePermissionsAsync(IdentityRole role, string roleName)
    {
        IList<Claim> existingClaims = await _roleManager.GetClaimsAsync(role);
        HashSet<string> existingPermissions = existingClaims
            .Where(c => c.Type == RolePermissions.ClaimType)
            .Select(c => c.Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (string permission in RolePermissions.GetForRole(roleName))
        {
            if (!existingPermissions.Contains(permission))
            {
                IdentityResult claimResult = await _roleManager.AddClaimAsync(role, new Claim(RolePermissions.ClaimType, permission));
                if (!claimResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Failed to seed permission '{permission}' for role '{roleName}': {string.Join("; ", claimResult.Errors.Select(e => e.Description))}");
                }
            }
        }
    }
}
