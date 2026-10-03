using ErrorOr;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Infrastructure.Auth;

public interface ITokenService
{
    Task<ErrorOr<TokenPair>> GenerateTokensAsync(AppIdentityUser user, CancellationToken ct = default);
    Task<ErrorOr<TokenPair>> RedeemTokenAsync(string refreshTokenPlain, CancellationToken ct = default);
}
