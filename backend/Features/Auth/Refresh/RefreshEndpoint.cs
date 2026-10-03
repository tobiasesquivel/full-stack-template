using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TemplateApp.Features.Auth.Refresh;
using TemplateApp.Infrastructure.Auth;

namespace TemplateApp.Features.Auth;

public partial class AuthController
{
    [HttpPost("tokens/refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> RefreshToken(
        [FromBody] RefreshRequest request,
        CancellationToken ct)
    {
        RefreshTokenCommand command = new(request.RefreshToken);
        ErrorOr<TokenPair> resultOrError = await _messageBus.InvokeAsync<ErrorOr<TokenPair>>(command, ct);
        return resultOrError.Match(
            tokens => Ok(new RefreshResponse(tokens.AccessToken, tokens.RefreshToken)),
            Problem);
    }
}
