using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Users;

public sealed class UserProfiles : IUserProfiles
{
    private readonly AppDbContext _db;

    public UserProfiles(AppDbContext db)
    {
        _db = db;
    }

    public async Task<ErrorOr<UserProfileSnapshot>> CreateAsync(string authId, string displayName, CancellationToken ct)
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

        return Snapshot(profile);
    }

    public async Task<UserProfileSnapshot?> FindAsync(string authId, CancellationToken ct)
    {
        UserProfile? profile = await _db.UserProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.AuthId == authId, ct);

        return profile is null ? null : Snapshot(profile);
    }

    public static UserProfileSnapshot Snapshot(UserProfile profile) =>
        new(profile.DisplayName, profile.Timezone);
}
