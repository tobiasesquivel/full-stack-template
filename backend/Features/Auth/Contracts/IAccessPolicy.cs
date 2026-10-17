using TemplateApp.Shared.Domain;

namespace TemplateApp.Features.Auth.Contracts;

public interface IAccessPolicy
{
    Task<IReadOnlySet<string>> GetPermissionsAsync(string userId, CancellationToken ct = default);

    Task<bool> HasPermissionAsync(string userId, string permission, CancellationToken ct = default);

    Task<bool> CanAccessAsync(string callerId, IOwnedResource resource, string permission, CancellationToken ct = default);
}
