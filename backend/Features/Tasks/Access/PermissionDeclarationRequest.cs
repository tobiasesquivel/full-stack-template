using TemplateApp.Infrastructure.Access;

namespace TemplateApp.Features.Tasks.Access;

internal sealed record PermissionDeclarationRequest(IReadOnlyList<PermissionGrant> Grants);
