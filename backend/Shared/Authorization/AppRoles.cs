namespace TemplateApp.Shared.Authorization;

public static class AppRoles
{
    public const string User = "User";
    public const string Admin = "Admin";
    public const string SuperAdmin = "SuperAdmin";

    public static IReadOnlyList<string> GetAll() => [User, Admin, SuperAdmin];
}
