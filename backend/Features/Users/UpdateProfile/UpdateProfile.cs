using ErrorOr;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Infrastructure.Persistence;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Users.UpdateProfile;

public sealed class UpdateProfile
{
    private static readonly UpdateProfileRequestValidator Validator = new();

    private readonly UserManager<AppIdentityUser> _users;
    private readonly AppDbContext _db;
    private readonly UserResponseBuilder _responses;

    public UpdateProfile(UserManager<AppIdentityUser> users, AppDbContext db, UserResponseBuilder responses)
    {
        _users = users;
        _db = db;
        _responses = responses;
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

        AppIdentityUser? identity = await _users.FindByIdAsync(callerId);
        if (identity is null)
            return UserErrors.NotFound(callerId);

        profile.DisplayName = request.DisplayName.Trim();
        profile.Timezone = request.Timezone;
        await _db.SaveChangesAsync(ct);

        return await _responses.BuildAsync(identity, profile, ct);
    }
}
