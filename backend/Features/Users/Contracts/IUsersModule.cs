using ErrorOr;

namespace TemplateApp.Features.Users.Contracts;

public interface IUsersModule
{
    Task<ErrorOr<UserProfileDetails>> CreateProfileAsync(string authId, string displayName, CancellationToken ct);

    Task<UserProfileDetails?> FindProfileAsync(string authId, CancellationToken ct);
}
