using System.Text.RegularExpressions;
using TemplateApp.Infrastructure.Access;

namespace TemplateApp.Features.Auth.Access;

internal static partial class PermissionDeclarationRules
{
    public const int MaxGrants = 100;

    public static IReadOnlyList<string> Problems(FeaturePermissions declaration)
    {
        List<string> problems = [];
        if (declaration.Feature is null || !Name().IsMatch(declaration.Feature))
        {
            problems.Add($"Feature '{declaration.Feature}' must be a PascalCase name of letters and digits.");
            return problems;
        }

        if (declaration.Grants is null)
        {
            problems.Add("Grants must not be null.");
            return problems;
        }

        if (declaration.Grants.Count > MaxGrants)
            problems.Add($"A feature declares at most {MaxGrants} permissions.");

        string prefix = FeaturePermissions.PrefixOf(declaration.Feature);
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (PermissionGrant? grant in declaration.Grants)
        {
            if (grant?.Permission is not { } permission
                || !permission.StartsWith(prefix, StringComparison.Ordinal)
                || !Name().IsMatch(permission[prefix.Length..]))
            {
                problems.Add($"Permission '{grant?.Permission}' must be {prefix}<Action>, with a PascalCase action: a feature declares only its own permissions.");
                continue;
            }

            if (!seen.Add(permission))
                problems.Add($"Permission '{permission}' is declared twice.");

            if (grant.Roles is null)
            {
                problems.Add($"Permission '{permission}' must list its roles.");
                continue;
            }

            foreach (string? role in grant.Roles.Where(role => !AppRoles.GetAll().Contains(role, StringComparer.Ordinal)))
                problems.Add($"Permission '{permission}' names the unknown role '{role}'.");
        }

        return problems;
    }

    [GeneratedRegex(@"\A[A-Z][A-Za-z0-9]{0,63}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Name();
}
