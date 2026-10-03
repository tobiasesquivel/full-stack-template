using System.Security.Claims;
using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TemplateApp.Domain.Authorization;
using TemplateApp.Features.Tasks.Common;
using TemplateApp.Features.Tasks.GetById;
using TemplateApp.Infrastructure.Authorization;

namespace TemplateApp.Features.Tasks;

public sealed partial class TasksController
{
    [HttpGet("{id:guid}")]
    [HasPermission(AppPermissions.Tasks.Read)]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken ct)
    {
        string? authId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (authId is null)
            return Unauthorized();

        GetTaskByIdQuery query = new(authId, id);
        ErrorOr<TaskItemResponse> resultOrError = await _bus.InvokeAsync<ErrorOr<TaskItemResponse>>(query, ct);

        return resultOrError.Match(Ok, Problem);
    }
}
