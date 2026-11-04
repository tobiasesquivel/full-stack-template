using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Users;

public sealed class UsersModule : IUsersModule
{
    private readonly AppDbContext _db;

    public UsersModule(AppDbContext db)
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
