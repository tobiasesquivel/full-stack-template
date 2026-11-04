using ErrorOr;
using Microsoft.AspNetCore.Identity;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Auth.Domain;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Auth.OpenSession;

public sealed class OpenSession
{
    private readonly UserManager<AppIdentityUser> _users;
    private readonly AppDbContext _db;
    private readonly Sessions _sessions;
    private readonly AuthModule _accounts;
    private readonly IUsersModule _profiles;

    public OpenSession(
        UserManager<AppIdentityUser> users,
        AppDbContext db,
        Sessions sessions,
        AuthModule accounts,
        IUsersModule profiles)
    {
        _users = users;
        _db = db;
        _sessions = sessions;
        _accounts = accounts;
        _profiles = profiles;
    }

    public async Task<ErrorOr<(UserResponse User, Session Session)>> ExecuteAsync(OpenSessionRequest request, CancellationToken ct)
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

        UserProfileDetails? profile = await _profiles.FindProfileAsync(identity.Id, ct);
        if (profile is null)
            return AuthErrors.ProfileMissing;

        Session session = _sessions.Open(identity.Id);
        await _db.SaveChangesAsync(ct);

        UserAccountDetails account = await _accounts.DescribeAsync(identity, ct);
        return (UserResponse.From(account, profile), session);
    }
}
