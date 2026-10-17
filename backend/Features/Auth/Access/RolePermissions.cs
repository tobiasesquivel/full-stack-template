using TemplateApp.Features.Tasks.Contracts;
using TemplateApp.Features.Users.Contracts;

namespace TemplateApp.Features.Auth.Access;

public static class RolePermissions
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
            ],
            AppRoles.Admin =>
            [
                UserPermissions.Read,
                TaskPermissions.Create,
                TaskPermissions.Read,
                TaskPermissions.Update,
                TaskPermissions.Delete,
            ],
            AppRoles.User => [TaskPermissions.Create],
            _ => [],
        };
}
