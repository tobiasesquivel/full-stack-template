using ErrorOr;
using Microsoft.AspNetCore.Identity;
using TemplateApp.Features.Auth.Access;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Shared.Domain;

namespace TemplateApp.Features.Auth;

internal sealed class AuthModule : IAuthModule
{
    private readonly UserManager<AppIdentityUser> _users;
    private readonly AccessPolicy _access;

    public AuthModule(UserManager<AppIdentityUser> users, AccessPolicy access)
    {
        _users = users;
        _access = access;
    }

    public Task<bool> HasPermissionAsync(string userId, string permission, CancellationToken ct) =>
        _access.HasPermissionAsync(userId, permission, ct);

    public Task<bool> CanAccessAsync(string callerId, IOwnedResource resource, string permission, CancellationToken ct) =>
        _access.CanAccessAsync(callerId, resource, permission, ct);

    public async Task<UserAccountDetails?> FindAccountAsync(string authId, CancellationToken ct)
    {
        AppIdentityUser? identity = await _users.FindByIdAsync(authId);
        return identity is null ? null : await DescribeAsync(identity, ct);
    }

    public async Task<UserAccountDetails> DescribeAsync(AppIdentityUser identity, CancellationToken ct)
    {
        AccessGrant grant = await _access.GetGrantAsync(identity.Id, ct);

        return new UserAccountDetails(
            identity.Id,
            identity.UserName,
            identity.Email,
            grant.Roles,
            grant.Permissions.ToList().AsReadOnly());
    }

    public async Task<ErrorOr<Success>> DeleteAccountAsync(string authId, CancellationToken ct)
    {
        AppIdentityUser? identity = await _users.FindByIdAsync(authId);
        if (identity is null)
            return AuthErrors.AccountNotFound;

        IdentityResult deleted = await _users.DeleteAsync(identity);
        if (!deleted.Succeeded)
            return deleted.Errors
                .Select(AuthErrors.AccountDeleteFailed)
                .ToList();

        return Result.Success;
    }
}
