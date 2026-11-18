using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Features.Users.Persistence;

namespace TemplateApp.Features.Users.GetUser;

internal sealed class GetUser
{
    private readonly UsersDbContext _db;
    private readonly IAuthModule _auth;

    public GetUser(UsersDbContext db, IAuthModule auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<ErrorOr<UserResponse>> ExecuteAsync(string callerId, string authId, CancellationToken ct)
    {
        UserProfile? profile = await _db.UserProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.AuthId == authId, ct);
        if (profile is null)
            return UserErrors.NotFound(authId);

        if (!await _auth.CanAccessAsync(callerId, profile, UserPermissions.Read, ct))
            return UserErrors.Forbidden;

        UserAccountDetails? account = await _auth.FindAccountAsync(authId, ct);
        if (account is null)
            return UserErrors.NotFound(authId);

        return UserResponse.From(account, UsersModule.Describe(profile));
    }
}
