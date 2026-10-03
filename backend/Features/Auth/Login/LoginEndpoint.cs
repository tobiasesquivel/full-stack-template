using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TemplateApp.Features.Auth.Common;
using TemplateApp.Features.Auth.Login;

namespace TemplateApp.Features.Auth;

public partial class AuthController
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken ct)
    {
        LoginUserCommand command = new(request.Username, request.Password);
        ErrorOr<AuthResponse> resultOrError = await _messageBus.InvokeAsync<ErrorOr<AuthResponse>>(command, ct);
        return resultOrError.Match(
            authResponse => Ok(authResponse),
            Problem);
    }
}
