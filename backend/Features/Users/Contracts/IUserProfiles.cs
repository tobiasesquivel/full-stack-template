using ErrorOr;

namespace TemplateApp.Features.Users.Contracts;

public interface IUserProfiles
{
    Task<ErrorOr<UserProfileSnapshot>> CreateAsync(string authId, string displayName, CancellationToken ct);

    Task<UserProfileSnapshot?> FindAsync(string authId, CancellationToken ct);
}
