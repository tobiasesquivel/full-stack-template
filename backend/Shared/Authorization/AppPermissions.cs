namespace TemplateApp.Shared.Authorization;

public static class AppPermissions
{
    public const string ClaimType = "permission";

    public static class Users
    {
        public const string Read = "Permissions.Users.Read";
        public const string Manage = "Permissions.Users.Manage";
    }

    public static class Tasks
    {
        public const string Create = "Permissions.Tasks.Create";
        public const string Read = "Permissions.Tasks.Read";
        public const string Update = "Permissions.Tasks.Update";
        public const string Delete = "Permissions.Tasks.Delete";
    }

    public static string[] GetForRole(string roleName) =>
        roleName switch
        {
            AppRoles.SuperAdmin =>
            [
                Users.Read,
                Users.Manage,
                Tasks.Create,
                Tasks.Read,
                Tasks.Update,
                Tasks.Delete,
            ],
            AppRoles.Admin =>
            [
                Users.Read,
                Tasks.Create,
                Tasks.Read,
                Tasks.Update,
                Tasks.Delete,
            ],
            AppRoles.User => [Tasks.Create],
            _ => [],
        };
}
