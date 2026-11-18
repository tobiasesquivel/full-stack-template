using TemplateApp.Features.Projects.Contracts;
using TemplateApp.Features.Users.Contracts;

namespace TemplateApp.Features.Auth.Access;

internal static class RolePermissions
{
    public const string ClaimType = "permission";

    public static string[] GetForRole(string roleName) =>
        roleName switch
        {
            AppRoles.SuperAdmin =>
            [
                UserPermissions.Read,
                UserPermissions.Manage,
                TaskPermissionNames.Create,
                TaskPermissionNames.Read,
                TaskPermissionNames.Update,
                TaskPermissionNames.Delete,
                ProjectPermissions.Create,
                ProjectPermissions.Read,
                ProjectPermissions.Update,
            ],
            AppRoles.Admin =>
            [
                UserPermissions.Read,
                TaskPermissionNames.Create,
                TaskPermissionNames.Read,
                TaskPermissionNames.Update,
                TaskPermissionNames.Delete,
                ProjectPermissions.Read,
                ProjectPermissions.Update,
            ],
            AppRoles.User => [TaskPermissionNames.Create, ProjectPermissions.Create],
            _ => [],
        };
}
