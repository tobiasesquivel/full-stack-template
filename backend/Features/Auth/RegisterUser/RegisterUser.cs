using ErrorOr;
using Microsoft.AspNetCore.Identity;
using TemplateApp.Features.Auth.Access;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Auth.Domain;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Auth.RegisterUser;

public sealed class RegisterUser
{
    private readonly UserManager<AppIdentityUser> _users;
    private readonly AppDbContext _db;
    private readonly Sessions _sessions;
    private readonly UserAccounts _accounts;
    private readonly IUserProfiles _profiles;

    public RegisterUser(
        UserManager<AppIdentityUser> users,
        AppDbContext db,
        Sessions sessions,
        UserAccounts accounts,
        IUserProfiles profiles)
    {
        _users = users;
        _db = db;
        _sessions = sessions;
        _accounts = accounts;
        _profiles = profiles;
    }

    public async Task<ErrorOr<(UserResponse User, Session Session)>> ExecuteAsync(RegisterUserRequest request, CancellationToken ct)
    {
        string userName = request.UserName.Trim();
        string email = request.Email.Trim();

        if (userName.Length is < 1 or > 100)
            return Error.Validation(code: "UserName", description: "UserName must be between 1 and 100 characters.");
        if (email.Length is < 3 or > 256 || !email.Contains('@'))
            return Error.Validation(code: "Email", description: "Email must be a valid email address.");
        if (request.Password.Length is < 6 or > 128)
            return Error.Validation(code: "Password", description: "Password must be between 6 and 128 characters.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        AppIdentityUser identity = new() { UserName = userName, Email = email };

        IdentityResult created = await _users.CreateAsync(identity, request.Password);
        if (!created.Succeeded)
            return MapIdentityErrors(created);

        IdentityResult roleResult = await _users.AddToRoleAsync(identity, AppRoles.User);
        if (!roleResult.Succeeded)
            return MapIdentityErrors(roleResult);

        ErrorOr<UserProfileSnapshot> profile = await _profiles.CreateAsync(identity.Id, request.DisplayName, ct);
        if (profile.IsError)
            return profile.Errors;

        Session session = _sessions.Open(identity.Id);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        UserAccount account = await _accounts.DescribeAsync(identity, ct);
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
                errors.Add(Error.Validation(code: e.Code, description: e.Description));
            }
        }

        return errors;
    }
}
