namespace TemplateApp.Infrastructure.Access;

public sealed record FeaturePermissions(string Feature, IReadOnlyList<PermissionGrant> Grants)
{
    public const string Prefix = "Permissions.";

    public static string PrefixOf(string feature) => Prefix + feature + ".";
}
