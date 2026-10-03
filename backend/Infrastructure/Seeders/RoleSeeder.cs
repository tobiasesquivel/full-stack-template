using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using TemplateApp.Domain.Authorization;

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
                await roleManager.CreateAsync(new IdentityRole(roleName));
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
                await roleManager.AddClaimAsync(role, new Claim(AppPermissions.ClaimType, permission));
            }
        }
    }
}