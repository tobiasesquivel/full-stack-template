using ErrorOr;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Infrastructure.Identity;
using TemplateApp.Infrastructure.Persistence;
using TemplateApp.Shared.Authorization;

namespace TemplateApp.Features.Auth;

public sealed class AuthModule
{
    private readonly UserManager<AppIdentityUser> _users;
    private readonly AppDbContext _db;
    private readonly TokenService _tokens;
    private readonly IPermissionService _permissions;
    private readonly TimeProvider _clock;

    public AuthModule(
        UserManager<AppIdentityUser> users,
        AppDbContext db,
        TokenService tokens,
        IPermissionService permissions,
        TimeProvider clock)
    {
        _users = users;
        _db = db;
        _tokens = tokens;
        _permissions = permissions;
        _clock = clock;
    }

    public async Task<ErrorOr<(AuthResponse Response, string RefreshPlaintext)>> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        string userName = request.UserName.Trim();
        string email = request.Email.Trim();
        string displayName = request.DisplayName.Trim();

        if (userName.Length is < 1 or > 100)
            return Error.Validation(code: "UserName", description: "UserName must be between 1 and 100 characters.");
        if (email.Length is < 3 or > 256 || !email.Contains('@'))
            return Error.Validation(code: "Email", description: "Email must be a valid email address.");
        if (request.Password.Length is < 6 or > 128)
            return Error.Validation(code: "Password", description: "Password must be between 6 and 128 characters.");
        if (displayName.Length is < 1 or > 200)
            return Error.Validation(code: "DisplayName", description: "DisplayName must be between 1 and 200 characters.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        AppIdentityUser identity = new() { UserName = userName, Email = email };

        IdentityResult created = await _users.CreateAsync(identity, request.Password);
        if (!created.Succeeded)
            return MapIdentityErrors(created);

        IdentityResult roleResult = await _users.AddToRoleAsync(identity, AppRoles.User);
        if (!roleResult.Succeeded)
            return MapIdentityErrors(roleResult);

        UserProfile profile = new() { AuthId = identity.Id, DisplayName = displayName, Timezone = "UTC" };
        _db.UserProfiles.Add(profile);

        IssuedSession session = await _tokens.IssueNewFamilyAsync(identity, ct);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        IList<string> roles = await _users.GetRolesAsync(identity);
        IReadOnlySet<string> permissions = await _permissions.GetPermissionsAsync(identity.Id, ct);

        AuthResponse response = _tokens.ToAuthResponse(
            identity,
            new UserProfileSnapshot(profile.DisplayName, profile.Timezone),
            roles,
            permissions,
            session);

        return (response, session.RefreshPlaintext);
    }

    public async Task<ErrorOr<(AuthResponse Response, string RefreshPlaintext)>> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        string userName = request.UserName.Trim();
        if (userName.Length is < 1 or > 100 || request.Password.Length is < 1 or > 128)
            return AuthErrors.InvalidCredentials;

        AppIdentityUser? identity = await _users.FindByNameAsync(userName);
        if (identity is null)
            return AuthErrors.InvalidCredentials;

        if (await _users.IsLockedOutAsync(identity))
            return AuthErrors.InvalidCredentials;

        bool valid = await _users.CheckPasswordAsync(identity, request.Password);
        if (!valid)
        {
            await _users.AccessFailedAsync(identity);
            return AuthErrors.InvalidCredentials;
        }

        await _users.ResetAccessFailedCountAsync(identity);

        UserProfile? profile = await _db.UserProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.AuthId == identity.Id, ct);
        if (profile is null)
            return AuthErrors.ProfileMissing;

        IssuedSession session = await _tokens.IssueNewFamilyAsync(identity, ct);
        await _db.SaveChangesAsync(ct);

        IList<string> roles = await _users.GetRolesAsync(identity);
        IReadOnlySet<string> permissions = await _permissions.GetPermissionsAsync(identity.Id, ct);

        AuthResponse response = _tokens.ToAuthResponse(
            identity,
            new UserProfileSnapshot(profile.DisplayName, profile.Timezone),
            roles,
            permissions,
            session);

        return (response, session.RefreshPlaintext);
    }

    public async Task<ErrorOr<(TokenResponse Response, string RefreshPlaintext)>> RefreshAsync(string refreshPlaintext, CancellationToken ct)
    {
        ErrorOr<IssuedSession> rotated = await _tokens.RotateAsync(refreshPlaintext, ct);
        if (rotated.IsError)
            return rotated.Errors;

        return (_tokens.ToTokenResponse(rotated.Value), rotated.Value.RefreshPlaintext);
    }

    public async Task LogoutAsync(string? refreshPlaintext, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(refreshPlaintext))
            return;

        await _tokens.RevokePresentedAsync(refreshPlaintext, ct);
    }

    private static List<Error> MapIdentityErrors(IdentityResult result)
    {
        List<Error> errors = [];
        foreach (IdentityError e in result.Errors)
        {
            if (string.Equals(e.Code, "DuplicateEmail", StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.Code, "DuplicateUserName", StringComparison.OrdinalIgnoreCase))
            {
                bool isEmail = e.Code.Contains("Email", StringComparison.OrdinalIgnoreCase);
                errors.Add(isEmail ? AuthErrors.EmailAlreadyExists : AuthErrors.UserNameAlreadyExists);
            }
            else
            {
                errors.Add(Error.Validation(code: e.Code, description: e.Description));
            }
        }

        return errors;
    }
}
