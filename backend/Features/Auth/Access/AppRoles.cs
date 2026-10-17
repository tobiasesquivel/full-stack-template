namespace TemplateApp.Features.Auth.Access;

public static class AppRoles
{
    public const string User = "User";
    public const string Admin = "Admin";
    public const string SuperAdmin = "SuperAdmin";

    public static IReadOnlyList<string> GetAll() => [User, Admin, SuperAdmin];
}
