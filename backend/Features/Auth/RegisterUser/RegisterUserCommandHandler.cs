using ErrorOr;
using Microsoft.AspNetCore.Identity;
using TemplateApp.Features.Auth.Access;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Auth.Domain;
using TemplateApp.Features.Auth.Persistence;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Infrastructure.Access;

namespace TemplateApp.Features.Auth.RegisterUser;

internal sealed partial record RegisterUserCommand
{
    public sealed class Handler
    {
        private readonly UserManager<AppIdentityUser> _users;
        private readonly Sessions _sessions;
        private readonly AuthModule _accounts;
        private readonly IUsersModule _profiles;

        public Handler(
            UserManager<AppIdentityUser> users,
            Sessions sessions,
            AuthModule accounts,
            IUsersModule profiles)
        {
            _users = users;
            _sessions = sessions;
            _accounts = accounts;
            _profiles = profiles;
        }

        public async Task<ErrorOr<(UserResponse User, Session Session)>> Handle(RegisterUserCommand command, CancellationToken ct)
        {
            string userName = command.UserName.Trim();
            string email = command.Email.Trim();

            AppIdentityUser identity = new() { UserName = userName, Email = email };

            IdentityResult created = await _users.CreateAsync(identity, command.Password);
            if (!created.Succeeded)
                return MapIdentityErrors(created);

            IdentityResult roleResult = await _users.AddToRoleAsync(identity, AppRoles.User);
            if (!roleResult.Succeeded)
                return MapIdentityErrors(roleResult);

            ErrorOr<UserProfileDetails> profile = await _profiles.CreateProfileAsync(identity.Id, command.DisplayName, ct);
            if (profile.IsError)
                return profile.Errors;

            Session session = _sessions.Open(identity.Id);

            UserAccountDetails account = await _accounts.DescribeAsync(identity, ct);
            return (UserResponse.From(account, profile.Value), session);
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
                    errors.Add(AuthErrors.IdentityRejected(e));
                }
            }

            return errors;
        }
    }
}
