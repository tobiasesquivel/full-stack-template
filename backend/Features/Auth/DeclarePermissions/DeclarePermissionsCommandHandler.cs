using ErrorOr;
using TemplateApp.Features.Auth.Access;
using TemplateApp.Infrastructure.Access;

namespace TemplateApp.Features.Auth.DeclarePermissions;

internal sealed partial record DeclarePermissionsCommand
{
    public sealed class Handler
    {
        private readonly PermissionCatalog _catalog;

        public Handler(PermissionCatalog catalog)
        {
            _catalog = catalog;
        }

        public async Task<ErrorOr<Success>> Handle(DeclarePermissionsCommand command, CancellationToken ct)
        {
            await _catalog.SyncAsync(new FeaturePermissions(command.Feature, command.Grants), ct);
            return Result.Success;
        }
    }
}
