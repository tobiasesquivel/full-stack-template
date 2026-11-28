using TemplateApp.Infrastructure.Access;
using TemplateApp.Infrastructure.Persistence.Initialization;

namespace TemplateApp.Features.Auth.Access;

internal sealed class RoleSeeder : IDatabaseSeeder
{
    private readonly PermissionCatalog _catalog;
    private readonly HostPermissions _declarations;

    public RoleSeeder(PermissionCatalog catalog, HostPermissions declarations)
    {
        _catalog = catalog;
        _declarations = declarations;
    }

    public async Task SeedAsync(CancellationToken ct)
    {
        await _catalog.EnsureRolesAsync(ct);
        foreach (FeaturePermissions declaration in _declarations.All)
            await _catalog.SyncAsync(declaration, ct);
    }
}
