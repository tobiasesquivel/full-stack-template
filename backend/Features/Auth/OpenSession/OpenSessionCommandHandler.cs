using ErrorOr;
using Microsoft.AspNetCore.Identity;
using TemplateApp.Features.Auth.Access;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Auth.Domain;
using TemplateApp.Features.Auth.Persistence;
using TemplateApp.Features.Auth.Telemetry;
using TemplateApp.Features.Users.Contracts;

namespace TemplateApp.Features.Auth.OpenSession;

internal sealed partial record OpenSessionCommand
{
    public sealed class Handler
    {
        private readonly UserManager<AppIdentityUser> _users;
        private readonly Sessions _sessions;
        private readonly AuthModule _accounts;
        private readonly IUsersModule _profiles;
        private readonly AuthMetrics _metrics;

        public Handler(
            UserManager<AppIdentityUser> users,
            Sessions sessions,
            AuthModule accounts,
            IUsersModule profiles,
            AuthMetrics metrics)
        {
            _users = users;
            _sessions = sessions;
            _accounts = accounts;
            _profiles = profiles;
            _metrics = metrics;
        }

        public async Task<ErrorOr<(UserResponse User, Session Session)>> Handle(OpenSessionCommand command, CancellationToken ct)
        {
            string userName = command.UserName.Trim();
            if (userName.Length is < 1 or > 100 || command.Password.Length is < 1 or > 128)
                return Rejected(LoginOutcome.InvalidCredentials);

            AppIdentityUser? identity = await _users.FindByNameAsync(userName);
            if (identity is null)
                return Rejected(LoginOutcome.InvalidCredentials);

            if (await _users.IsLockedOutAsync(identity))
                return Rejected(LoginOutcome.LockedOut);

            bool valid = await _users.CheckPasswordAsync(identity, command.Password);
            if (!valid)
            {
                await _users.AccessFailedAsync(identity);
                return Rejected(LoginOutcome.InvalidCredentials);
            }

            await _users.ResetAccessFailedCountAsync(identity);

            UserProfileDetails? profile = await _profiles.FindProfileAsync(identity.Id, ct);
            if (profile is null)
                return AuthErrors.ProfileMissing;

            Session session = _sessions.Open(identity.Id);
            _metrics.LoginAttempted(LoginOutcome.Success);

            UserAccountDetails account = await _accounts.DescribeAsync(identity, ct);
            return (UserResponse.From(account, profile), session);
        }

        private Error Rejected(LoginOutcome outcome)
        {
            _metrics.LoginAttempted(outcome);
            return AuthErrors.InvalidCredentials;
        }
    }
}
