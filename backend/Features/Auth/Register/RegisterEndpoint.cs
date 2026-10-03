using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TemplateApp.Features.Auth.Common;
using TemplateApp.Features.Auth.Register;

namespace TemplateApp.Features.Auth;

public partial class AuthController
{
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request,
        CancellationToken ct)
    {
        RegisterUserCommand command = new(request.Email, request.Password, request.UserName);
        ErrorOr<AuthResponse> resultOrError = await _messageBus.InvokeAsync<ErrorOr<AuthResponse>>(command, ct);
        return resultOrError.Match(
            authResponse => CreatedAtRoute(
                "GetUserById",
                new { authId = authResponse.User.AuthId },
                authResponse),
            Problem);
    }
}
