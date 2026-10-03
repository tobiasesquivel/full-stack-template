using System.Security.Claims;
using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TemplateApp.Domain.Authorization;
using TemplateApp.Features.Tasks.Delete;
using TemplateApp.Infrastructure.Authorization;

namespace TemplateApp.Features.Tasks;

public sealed partial class TasksController
{
    [HttpDelete("{id:guid}")]
    [HasPermission(AppPermissions.Tasks.Delete)]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken ct)
    {
        string? authId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (authId is null)
            return Unauthorized();

        DeleteTaskCommand command = new(authId, id);
        ErrorOr<Success> resultOrError = await _bus.InvokeAsync<ErrorOr<Success>>(command, ct);

        return resultOrError.Match(_ => NoContent(), Problem);
    }
}
