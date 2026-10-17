using ErrorOr;
using Microsoft.AspNetCore.Identity;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Auth.OpenSession;

public sealed class OpenSession
{
    private readonly UserManager<AppIdentityUser> _users;
    private readonly AppDbContext _db;
    private readonly TokenService _tokens;
    private readonly UserAccounts _accounts;
    private readonly IUserProfiles _profiles;

    public OpenSession(
        UserManager<AppIdentityUser> users,
        AppDbContext db,
        TokenService tokens,
        UserAccounts accounts,
        IUserProfiles profiles)
    {
        _users = users;
        _db = db;
        _tokens = tokens;
        _accounts = accounts;
        _profiles = profiles;
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

        UserProfileSnapshot? profile = await _profiles.FindAsync(identity.Id, ct);
        if (profile is null)
            return AuthErrors.ProfileMissing;

        IssuedSession session = await _tokens.IssueNewFamilyAsync(identity, ct);
        await _db.SaveChangesAsync(ct);

        UserAccount account = await _accounts.DescribeAsync(identity, ct);
        AuthResponse response = _tokens.ToAuthResponse(account, profile, session);

        return (response, session.RefreshPlaintext);
    }
}
