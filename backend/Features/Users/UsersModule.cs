using ErrorOr;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Users;

public sealed class UsersModule
{
    private readonly UserManager<AppIdentityUser> _users;
    private readonly AppDbContext _db;
    private readonly IAccessPolicy _access;
    private readonly UpdateProfileRequestValidator _updateValidator = new();

    public UsersModule(
        UserManager<AppIdentityUser> users,
        AppDbContext db,
        IAccessPolicy access)
    {
        _users = users;
        _db = db;
        _access = access;
    }

    public async Task<ErrorOr<UserResponse>> GetMeAsync(string callerId, CancellationToken ct)
    {
        AppIdentityUser? identity = await _users.FindByIdAsync(callerId);
        if (identity is null)
            return UserErrors.NotFound(callerId);

        UserProfile? profile = await _db.UserProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.AuthId == callerId, ct);
        if (profile is null)
            return UserErrors.NotFound(callerId);

        return await ToResponseAsync(identity, profile, ct);
    }

    public async Task<ErrorOr<UserResponse>> UpdateMeAsync(string callerId, UpdateProfileRequest request, CancellationToken ct)
    {
        var validation = await _updateValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return validation.Errors
                .Select(f => Error.Validation(code: f.PropertyName, description: f.ErrorMessage))
                .ToList();

        UserProfile? profile = await _db.UserProfiles
            .SingleOrDefaultAsync(p => p.AuthId == callerId, ct);
        if (profile is null)
            return UserErrors.NotFound(callerId);

        AppIdentityUser? identity = await _users.FindByIdAsync(callerId);
        if (identity is null)
            return UserErrors.NotFound(callerId);

        profile.DisplayName = request.DisplayName.Trim();
        profile.Timezone = request.Timezone;
        await _db.SaveChangesAsync(ct);

        return await ToResponseAsync(identity, profile, ct);
    }

    public async Task<ErrorOr<UserResponse>> GetByIdAsync(string callerId, string authId, CancellationToken ct)
    {
        UserProfile? profile = await _db.UserProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.AuthId == authId, ct);
        if (profile is null)
            return UserErrors.NotFound(authId);

        if (!await _access.CanAccessAsync(callerId, profile, UserPermissions.Read, ct))
            return Error.Forbidden(code: "User.Forbidden", description: "Access denied.");

        AppIdentityUser? identity = await _users.FindByIdAsync(authId);
        if (identity is null)
            return UserErrors.NotFound(authId);

        return await ToResponseAsync(identity, profile, ct);
    }

    public async Task<ErrorOr<Success>> DeleteByIdAsync(string callerId, string authId, CancellationToken ct)
    {
        UserProfile? profile = await _db.UserProfiles
            .SingleOrDefaultAsync(p => p.AuthId == authId, ct);
        if (profile is null)
            return UserErrors.NotFound(authId);

        if (!await _access.CanAccessAsync(callerId, profile, UserPermissions.Manage, ct))
            return Error.Forbidden(code: "User.Forbidden", description: "Access denied.");

        AppIdentityUser? identity = await _users.FindByIdAsync(authId);
        if (identity is null)
            return UserErrors.NotFound(authId);

        IdentityResult deleted = await _users.DeleteAsync(identity);
        if (!deleted.Succeeded)
            return deleted.Errors
                .Select(e => Error.Failure(code: e.Code, description: e.Description))
                .ToList();

        return Result.Success;
    }

    private async Task<UserResponse> ToResponseAsync(AppIdentityUser identity, UserProfile profile, CancellationToken ct)
    {
        IList<string> roles = await _users.GetRolesAsync(identity);
        IReadOnlySet<string> permissions = await _access.GetPermissionsAsync(identity.Id, ct);

        return new UserResponse(
            identity.Id,
            identity.UserName,
            identity.Email,
            profile.DisplayName,
            profile.Timezone,
            new List<string>(roles).AsReadOnly(),
            permissions.ToList().AsReadOnly());
    }
}
