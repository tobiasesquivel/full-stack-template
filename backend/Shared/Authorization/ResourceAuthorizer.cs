using TemplateApp.Shared.Domain;

namespace TemplateApp.Shared.Authorization;

public sealed class ResourceAuthorizer
{
    private readonly IPermissionService _permissions;

    public ResourceAuthorizer(IPermissionService permissions)
    {
        _permissions = permissions;
    }

    public async Task<bool> CanAccessAsync(
        string callerId,
        IOwnedResource resource,
        string permission,
        CancellationToken ct = default)
    {
        if (string.Equals(callerId, resource.OwnerId, StringComparison.Ordinal))
            return true;

        return await _permissions.HasPermissionAsync(callerId, permission, ct);
    }
}
