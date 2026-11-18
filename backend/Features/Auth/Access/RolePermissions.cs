using TemplateApp.Features.Projects.Contracts;
using TemplateApp.Features.Tasks.Contracts;
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
                TaskPermissions.Create,
                TaskPermissions.Read,
                TaskPermissions.Update,
                TaskPermissions.Delete,
                ProjectPermissions.Create,
                ProjectPermissions.Read,
                ProjectPermissions.Update,
            ],
            AppRoles.Admin =>
            [
                UserPermissions.Read,
                TaskPermissions.Create,
                TaskPermissions.Read,
                TaskPermissions.Update,
                TaskPermissions.Delete,
                ProjectPermissions.Read,
                ProjectPermissions.Update,
            ],
            AppRoles.User => [TaskPermissions.Create, ProjectPermissions.Create],
            _ => [],
        };
}
