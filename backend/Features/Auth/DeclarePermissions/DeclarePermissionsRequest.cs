using TemplateApp.Infrastructure.Access;

namespace TemplateApp.Features.Auth.DeclarePermissions;

internal sealed record DeclarePermissionsRequest(IReadOnlyList<PermissionGrant> Grants);
