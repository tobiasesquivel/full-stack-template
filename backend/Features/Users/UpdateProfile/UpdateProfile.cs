using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Infrastructure.Persistence;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Users.UpdateProfile;

internal sealed class UpdateProfile
{
    private static readonly UpdateProfileRequestValidator Validator = new();

    private readonly AppDbContext _db;
    private readonly IAuthModule _auth;

    public UpdateProfile(AppDbContext db, IAuthModule auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<ErrorOr<UserResponse>> ExecuteAsync(string callerId, UpdateProfileRequest request, CancellationToken ct)
    {
        var validation = await Validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return ValidationErrors.From(validation);

        UserProfile? profile = await _db.UserProfiles
            .SingleOrDefaultAsync(p => p.AuthId == callerId, ct);
        if (profile is null)
            return UserErrors.NotFound(callerId);

        UserAccountDetails? account = await _auth.FindAccountAsync(callerId, ct);
        if (account is null)
            return UserErrors.NotFound(callerId);

        profile.DisplayName = UserProfile.NormalizeDisplayName(request.DisplayName);
        profile.Timezone = request.Timezone;
        await _db.SaveChangesAsync(ct);

        return UserResponse.From(account, UsersModule.Describe(profile));
    }
}
