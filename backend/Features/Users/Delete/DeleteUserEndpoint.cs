using System.Security.Claims;
using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TemplateApp.Domain.Authorization;
using TemplateApp.Features.Users.Delete;
using TemplateApp.Infrastructure.Authorization;

namespace TemplateApp.Features.Users;

public sealed partial class UsersController
{
    [HttpDelete("{userId}")]
    [HasPermission(AppPermissions.Users.Manage)]
    public async Task<IActionResult> DeleteUser(string userId, CancellationToken ct)
    {
        string? authenticatedUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (authenticatedUserId != userId)
            return Forbid();

        DeleteUserCommand command = new(userId);
        ErrorOr<Success> resultOrError = await _bus.InvokeAsync<ErrorOr<Success>>(command, ct);
        return resultOrError.Match(
            _ => NoContent(),
            Problem);
    }
}
