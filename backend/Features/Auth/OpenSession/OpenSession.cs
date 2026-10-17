using ErrorOr;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Auth.OpenSession;

public sealed class OpenSession
{
    private readonly UserManager<AppIdentityUser> _users;
    private readonly AppDbContext _db;
    private readonly TokenService _tokens;
    private readonly IAccessPolicy _access;

    public OpenSession(
        UserManager<AppIdentityUser> users,
        AppDbContext db,
        TokenService tokens,
        IAccessPolicy access)
    {
        _users = users;
        _db = db;
        _tokens = tokens;
        _access = access;
    }

    public async Task<ErrorOr<(AuthResponse Response, string RefreshPlaintext)>> ExecuteAsync(OpenSessionRequest request, CancellationToken ct)
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
        IReadOnlySet<string> permissions = await _access.GetPermissionsAsync(identity.Id, ct);

        AuthResponse response = _tokens.ToAuthResponse(
            identity,
            new UserProfileSnapshot(profile.DisplayName, profile.Timezone),
            roles,
            permissions,
            session);

        return (response, session.RefreshPlaintext);
    }
}
