using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Shared.Domain;

namespace TemplateApp.Features.Auth.Access;

public sealed class AccessPolicy
{
    private readonly UserManager<AppIdentityUser> _users;
    private readonly RoleManager<IdentityRole> _roles;
    private readonly Dictionary<string, AccessGrant> _grants = new(StringComparer.Ordinal);

    public AccessPolicy(UserManager<AppIdentityUser> users, RoleManager<IdentityRole> roles)
    {
        _users = users;
        _roles = roles;
    }

    public async Task<AccessGrant> GetGrantAsync(string userId, CancellationToken ct = default)
    {
        if (_grants.TryGetValue(userId, out AccessGrant? known))
            return known;

        AccessGrant grant = await LoadGrantAsync(userId, ct);
        _grants[userId] = grant;
        return grant;
    }

    public async Task<bool> HasPermissionAsync(string userId, string permission, CancellationToken ct = default)
    {
        AccessGrant grant = await GetGrantAsync(userId, ct);
        return grant.Permissions.Contains(permission);
    }

    public async Task<bool> CanAccessAsync(
        string callerId,
        IOwnedResource resource,
        string permission,
        CancellationToken ct = default)
    {
        if (string.Equals(callerId, resource.OwnerId, StringComparison.Ordinal))
            return true;

        return await HasPermissionAsync(callerId, permission, ct);
    }

    private async Task<AccessGrant> LoadGrantAsync(string userId, CancellationToken ct)
    {
        AppIdentityUser? user = await _users.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return AccessGrant.None;

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
                if (claim.Type == RolePermissions.ClaimType)
                    permissions.Add(claim.Value);
            }
        }

        return new AccessGrant(roleNames.ToList().AsReadOnly(), permissions);
    }
}
