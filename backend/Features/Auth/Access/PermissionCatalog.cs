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

    public async Task GrantAsync(FeaturePermissions declaration, CancellationToken ct)
    {
        IReadOnlyList<string> problems = PermissionDeclarationRules.Problems(declaration);
        if (problems.Count > 0)
            throw new InvalidOperationException($"The permissions of {declaration.Feature} are invalid: {string.Join("; ", problems)}");

        using TransactionScope scope = CrossContextTransaction.Begin();
        await LockAsync(ct);
        foreach (string roleName in AppRoles.GetAll())
        {
            IdentityRole role = await EnsureRoleAsync(roleName);
            HashSet<string> held = (await _roles.GetClaimsAsync(role))
                .Where(claim => claim.Type == ClaimType)
                .Select(claim => claim.Value)
                .ToHashSet(StringComparer.Ordinal);
            IEnumerable<string> missing = declaration.Grants
                .Where(grant => grant.Roles.Contains(roleName, StringComparer.Ordinal) && !held.Contains(grant.Permission))
                .Select(grant => grant.Permission)
                .Order(StringComparer.Ordinal);

            foreach (string permission in missing)
                Ensure(await _roles.AddClaimAsync(role, new Claim(ClaimType, permission)), $"grant permission '{permission}' to role '{roleName}'");
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
