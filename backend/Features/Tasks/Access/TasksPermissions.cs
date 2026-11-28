using TemplateApp.Infrastructure.Access;

namespace TemplateApp.Features.Tasks.Access;

internal sealed class TasksPermissions : IFeaturePermissions
{
    public const string Create = "Permissions.Tasks.Create";
    public const string Read = "Permissions.Tasks.Read";
    public const string Update = "Permissions.Tasks.Update";
    public const string Delete = "Permissions.Tasks.Delete";

    public IReadOnlyList<PermissionGrant> Grants { get; } =
    [
        new(Create, [AppRoles.User, AppRoles.Admin, AppRoles.SuperAdmin]),
        new(Read, [AppRoles.Admin, AppRoles.SuperAdmin]),
        new(Update, [AppRoles.Admin, AppRoles.SuperAdmin]),
        new(Delete, [AppRoles.Admin, AppRoles.SuperAdmin]),
    ];
}
