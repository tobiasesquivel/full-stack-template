namespace TemplateApp.Domain.Authorization;

public static class AppRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Admin = "Admin";
    public const string User = "User";
    public static IReadOnlyList<string> GetAll() => [SuperAdmin, Admin, User];
}
