namespace TemplateApp.Infrastructure.Access;

public sealed record PermissionGrant(string Permission, IReadOnlyList<string> Roles);
