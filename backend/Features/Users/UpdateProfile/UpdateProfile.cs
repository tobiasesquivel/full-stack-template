using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Features.Users.Persistence;

namespace TemplateApp.Features.Users.UpdateProfile;

internal static class UpdateProfile
{
    public sealed class Handler
    {
        private readonly UsersDbContext _db;
        private readonly IAuthModule _auth;

        public Handler(UsersDbContext db, IAuthModule auth)
        {
            _db = db;
            _auth = auth;
        }

        public async Task<ErrorOr<UserResponse>> Handle(UpdateProfileCommand command, CancellationToken ct)
        {
            UserProfile? profile = await _db.UserProfiles
                .SingleOrDefaultAsync(p => p.AuthId == command.CallerId, ct);
            if (profile is null)
                return UserErrors.NotFound(command.CallerId);

            UserAccountDetails? account = await _auth.FindAccountAsync(command.CallerId, ct);
            if (account is null)
                return UserErrors.NotFound(command.CallerId);

            profile.DisplayName = UserProfile.NormalizeDisplayName(command.DisplayName);
            profile.Timezone = command.Timezone;
            return UserResponse.From(account, UsersModule.Describe(profile));
        }
    }
}
