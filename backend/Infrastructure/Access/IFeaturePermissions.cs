namespace TemplateApp.Infrastructure.Access;

public interface IFeaturePermissions
{
    IReadOnlyList<PermissionGrant> Grants { get; }
}
