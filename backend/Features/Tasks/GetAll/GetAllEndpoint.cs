using System.Security.Claims;
using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TemplateApp.Domain.Authorization;
using TemplateApp.Features.Tasks.GetAll;
using TemplateApp.Infrastructure.Authorization;

namespace TemplateApp.Features.Tasks;

public sealed partial class TasksController
{
    [HttpGet]
    [HasPermission(AppPermissions.Tasks.Read)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        string? authId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (authId is null)
            return Unauthorized();

        GetAllTasksQuery query = new(authId);
        ErrorOr<GetAllTasksQueryResult> resultOrError = await _bus.InvokeAsync<
            ErrorOr<GetAllTasksQueryResult>
        >(query, ct);

        return resultOrError.Match(Ok, Problem);
    }
}
