using ErrorOr;

namespace TemplateApp.Features.Auth.Contracts;

public interface IUserAccounts
{
    Task<UserAccount?> FindAsync(string authId, CancellationToken ct);

    Task<ErrorOr<Success>> DeleteAsync(string authId, CancellationToken ct);
}
