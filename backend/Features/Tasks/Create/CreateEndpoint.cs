using System.Security.Claims;
using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TemplateApp.Domain.Authorization;
using TemplateApp.Features.Tasks.Common;
using TemplateApp.Features.Tasks.Create;
using TemplateApp.Infrastructure.Authorization;

namespace TemplateApp.Features.Tasks;

public sealed partial class TasksController
{
    [HttpPost]
    [HasPermission(AppPermissions.Tasks.Create)]
    public async Task<IActionResult> Create(
        [FromBody] CreateTaskRequest request,
        CancellationToken ct)
    {
        string? authId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (authId is null)
            return Unauthorized();

        CreateTaskCommand command = new(authId, request.Title);
        ErrorOr<TaskItemResponse> resultOrError = await _bus.InvokeAsync<ErrorOr<TaskItemResponse>>(command, ct);

        return resultOrError.Match(
            response => CreatedAtAction(
                nameof(GetById),
                new { id = response.Id },
                response),
            Problem);
    }
}
