using TemplateApp.Shared.Domain;

namespace TemplateApp.Features.Auth.Contracts;

public interface IAuthModule
{
    Task<bool> HasPermissionAsync(string userId, string permission, CancellationToken ct);

    Task<bool> CanAccessAsync(string callerId, IOwnedResource resource, string permission, CancellationToken ct);

    Task<UserAccountDetails?> FindAccountAsync(string authId, CancellationToken ct);
}
