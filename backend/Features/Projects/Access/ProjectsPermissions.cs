using TemplateApp.Infrastructure.Access;

namespace TemplateApp.Features.Projects.Access;

internal sealed class ProjectsPermissions : IFeaturePermissions
{
    public const string Create = "Permissions.Projects.Create";
    public const string Read = "Permissions.Projects.Read";
    public const string Update = "Permissions.Projects.Update";

    public IReadOnlyList<PermissionGrant> Grants { get; } =
    [
        new(Create, [AppRoles.User, AppRoles.SuperAdmin]),
        new(Read, [AppRoles.Admin, AppRoles.SuperAdmin]),
        new(Update, [AppRoles.Admin, AppRoles.SuperAdmin]),
    ];
}
