using ErrorOr;
using TemplateApp.Infrastructure.Auth;

namespace TemplateApp.Features.Auth.Refresh;

public sealed class RefreshTokenCommandHandler
{
    private readonly ITokenService _tokenService;

    public RefreshTokenCommandHandler(ITokenService tokenService)
    {
        _tokenService = tokenService;
    }

    public async Task<ErrorOr<TokenPair>> Handle(RefreshTokenCommand command, CancellationToken ct)
    {
        return await _tokenService.RedeemTokenAsync(command.RefreshToken, ct);
    }
}
