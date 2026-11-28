using TemplateApp.Infrastructure.Access;

namespace TemplateApp.Features.Auth.DeclarePermissions;

internal sealed partial record DeclarePermissionsCommand(string Feature, IReadOnlyList<PermissionGrant> Grants);
