using TemplateApp.Infrastructure.Access;

namespace TemplateApp.Features.Users.Access;

internal sealed class UsersPermissions : IFeaturePermissions
{
    public const string Read = "Permissions.Users.Read";
    public const string Manage = "Permissions.Users.Manage";

    public IReadOnlyList<PermissionGrant> Grants { get; } =
    [
        new(Read, [AppRoles.Admin, AppRoles.SuperAdmin]),
        new(Manage, [AppRoles.SuperAdmin]),
    ];
}
