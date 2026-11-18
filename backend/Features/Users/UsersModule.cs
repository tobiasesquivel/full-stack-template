using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Features.Users.Persistence;

namespace TemplateApp.Features.Users;

internal sealed class UsersModule : IUsersModule
{
    private readonly UsersDbContext _db;

    public UsersModule(UsersDbContext db)
    {
        _db = db;
    }

    public async Task<ErrorOr<UserProfileDetails>> CreateProfileAsync(string authId, string displayName, CancellationToken ct)
    {
        if (!UserProfile.IsValidDisplayName(displayName))
            return UserErrors.InvalidDisplayName;

        UserProfile profile = new()
        {
            AuthId = authId,
            DisplayName = UserProfile.NormalizeDisplayName(displayName),
            Timezone = "UTC",
        };
        _db.UserProfiles.Add(profile);
        await _db.SaveChangesAsync(ct);

        return Describe(profile);
    }

    public async Task<UserProfileDetails?> FindProfileAsync(string authId, CancellationToken ct)
    {
        UserProfile? profile = await _db.UserProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.AuthId == authId, ct);

        return profile is null ? null : Describe(profile);
    }

    public static UserProfileDetails Describe(UserProfile profile) =>
        new(profile.DisplayName, profile.Timezone);
}
