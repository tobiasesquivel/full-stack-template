using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Infrastructure.Identity;

namespace TemplateApp.Shared.Authorization;

public interface IPermissionService
{
    Task<IReadOnlySet<string>> GetPermissionsAsync(string userId, CancellationToken ct = default);
    Task<bool> HasPermissionAsync(string userId, string permission, CancellationToken ct = default);
}

public sealed class PermissionService : IPermissionService
{
    private readonly UserManager<AppIdentityUser> _users;
    private readonly RoleManager<IdentityRole> _roles;

    public PermissionService(UserManager<AppIdentityUser> users, RoleManager<IdentityRole> roles)
    {
        _users = users;
        _roles = roles;
    }

    public async Task<IReadOnlySet<string>> GetPermissionsAsync(string userId, CancellationToken ct = default)
    {
        AppIdentityUser? user = await _users.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return new HashSet<string>(StringComparer.Ordinal);

        IList<string> roleNames = await _users.GetRolesAsync(user);
        HashSet<string> permissions = new(StringComparer.Ordinal);

        foreach (string roleName in roleNames.Distinct(StringComparer.Ordinal))
        {
            IdentityRole? role = await _roles.FindByNameAsync(roleName);
            if (role is null)
                continue;

            IList<Claim> claims = await _roles.GetClaimsAsync(role);
            foreach (Claim claim in claims)
            {
                if (claim.Type == AppPermissions.ClaimType)
                    permissions.Add(claim.Value);
            }
        }

        return permissions;
    }

    public async Task<bool> HasPermissionAsync(string userId, string permission, CancellationToken ct = default)
    {
        IReadOnlySet<string> permissions = await GetPermissionsAsync(userId, ct);
        return permissions.Contains(permission);
    }
}
