using System.Security.Claims;
using System.Transactions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Persistence;
using TemplateApp.Infrastructure.Access;
using TemplateApp.Infrastructure.Persistence.Transactions;

namespace TemplateApp.Features.Auth.Access;

internal sealed class PermissionCatalog
{
    public const string ClaimType = "permission";
    public const long LockKey = 7_164_253_002;

    private readonly AuthDbContext _db;
    private readonly RoleManager<IdentityRole> _roles;

    public PermissionCatalog(AuthDbContext db, RoleManager<IdentityRole> roles)
    {
        _db = db;
        _roles = roles;
    }

    public async Task EnsureRolesAsync(CancellationToken ct)
    {
        using TransactionScope scope = CrossContextTransaction.Begin();
        await LockAsync(ct);
        foreach (string roleName in AppRoles.GetAll())
            await EnsureRoleAsync(roleName);
        scope.Complete();
    }

    public async Task SyncAsync(FeaturePermissions declaration, CancellationToken ct)
    {
        IReadOnlyList<string> problems = PermissionDeclarationRules.Problems(declaration);
        if (problems.Count > 0)
            throw new InvalidOperationException($"The permissions of {declaration.Feature} are invalid: {string.Join("; ", problems)}");

        using TransactionScope scope = CrossContextTransaction.Begin();
        await LockAsync(ct);
        string prefix = FeaturePermissions.PrefixOf(declaration.Feature);
        foreach (string roleName in AppRoles.GetAll())
        {
            IdentityRole role = await EnsureRoleAsync(roleName);
            HashSet<string> wanted = declaration.Grants
                .Where(grant => grant.Roles.Contains(roleName, StringComparer.Ordinal))
                .Select(grant => grant.Permission)
                .ToHashSet(StringComparer.Ordinal);
            List<Claim> held = (await _roles.GetClaimsAsync(role))
                .Where(claim => claim.Type == ClaimType && claim.Value.StartsWith(prefix, StringComparison.Ordinal))
                .ToList();

            foreach (Claim stale in held.Where(claim => !wanted.Contains(claim.Value)))
                Ensure(await _roles.RemoveClaimAsync(role, stale), $"remove permission '{stale.Value}' from role '{roleName}'");

            foreach (string missing in wanted.Except(held.Select(claim => claim.Value), StringComparer.Ordinal).Order(StringComparer.Ordinal))
                Ensure(await _roles.AddClaimAsync(role, new Claim(ClaimType, missing)), $"grant permission '{missing}' to role '{roleName}'");
        }

        scope.Complete();
    }

    private Task<int> LockAsync(CancellationToken ct) =>
        _db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({LockKey})", ct);

    private async Task<IdentityRole> EnsureRoleAsync(string roleName)
    {
        IdentityRole? role = await _roles.FindByNameAsync(roleName);
        if (role is not null)
            return role;

        role = new IdentityRole(roleName);
        Ensure(await _roles.CreateAsync(role), $"seed role '{roleName}'");
        return role;
    }

    private static void Ensure(IdentityResult result, string action)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException($"Failed to {action}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
    }
}
