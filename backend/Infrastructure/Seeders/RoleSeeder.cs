using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using TemplateApp.Shared.Authorization;

namespace TemplateApp.Infrastructure.Seeders;

public static class RoleSeeder
{
    public static async Task SeedRolesAsync(IServiceProvider serviceProvider)
    {
        RoleManager<IdentityRole> roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        foreach (string roleName in AppRoles.GetAll())
        {
            bool roleExists = await roleManager.RoleExistsAsync(roleName);
            if (!roleExists)
            {
                IdentityResult createdResult = await roleManager.CreateAsync(new IdentityRole(roleName));
                if (!createdResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Failed to seed role '{roleName}': {string.Join("; ", createdResult.Errors.Select(e => e.Description))}");
                }
            }

            IdentityRole? role = await roleManager.FindByNameAsync(roleName);
            if (role is null)
            {
                continue;
            }

            await SeedRolePermissionsAsync(roleManager, role, roleName);
        }
    }

    private static async Task SeedRolePermissionsAsync(
        RoleManager<IdentityRole> roleManager, IdentityRole role, string roleName)
    {
        IList<Claim> existingClaims = await roleManager.GetClaimsAsync(role);
        HashSet<string> existingPermissions = existingClaims
            .Where(c => c.Type == AppPermissions.ClaimType)
            .Select(c => c.Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (string permission in AppPermissions.GetForRole(roleName))
        {
            if (!existingPermissions.Contains(permission))
            {
                IdentityResult claimResult = await roleManager.AddClaimAsync(role, new Claim(AppPermissions.ClaimType, permission));
                if (!claimResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Failed to seed permission '{permission}' for role '{roleName}': {string.Join("; ", claimResult.Errors.Select(e => e.Description))}");
                }
            }
        }
    }
}