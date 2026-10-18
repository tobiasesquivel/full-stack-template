using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Users.GetUser;

public sealed class GetUser
{
    private readonly AppDbContext _db;
    private readonly IAccessPolicy _access;
    private readonly IUserAccounts _accounts;

    public GetUser(AppDbContext db, IAccessPolicy access, IUserAccounts accounts)
    {
        _db = db;
        _access = access;
        _accounts = accounts;
    }

    public async Task<ErrorOr<UserResponse>> ExecuteAsync(string callerId, string authId, CancellationToken ct)
    {
        UserProfile? profile = await _db.UserProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.AuthId == authId, ct);
        if (profile is null)
            return UserErrors.NotFound(authId);

        if (!await _access.CanAccessAsync(callerId, profile, UserPermissions.Read, ct))
            return UserErrors.Forbidden;

        UserAccount? account = await _accounts.FindAsync(authId, ct);
        if (account is null)
            return UserErrors.NotFound(authId);

        return UserResponse.From(account, UserProfiles.Snapshot(profile));
    }
}
