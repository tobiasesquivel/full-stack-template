using System.Security.Claims;
using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TemplateApp.Domain.Authorization;
using TemplateApp.Features.Tasks.Common;
using TemplateApp.Features.Tasks.Toggle;
using TemplateApp.Infrastructure.Authorization;

namespace TemplateApp.Features.Tasks;

public sealed partial class TasksController
{
    [HttpPatch("{id:guid}/toggle")]
    [HasPermission(AppPermissions.Tasks.Update)]
    public async Task<IActionResult> Toggle(
        Guid id,
        CancellationToken ct)
    {
        string? authId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (authId is null)
            return Unauthorized();

        ToggleTaskCommand command = new(authId, id);
        ErrorOr<TaskItemResponse> resultOrError = await _bus.InvokeAsync<ErrorOr<TaskItemResponse>>(command, ct);

        return resultOrError.Match(Ok, Problem);
    }
}
