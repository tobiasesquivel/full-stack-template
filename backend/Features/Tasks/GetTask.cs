using System.Security.Claims;
using ErrorOr;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Tasks.Contracts;
using TemplateApp.Shared.Authorization;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Tasks;

public sealed partial class TasksModule
{
    public async Task<ErrorOr<TaskResponse>> GetByIdAsync(string callerId, Guid id, CancellationToken ct)
    {
        var item = await _db.TaskItems.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (item is null)
            return TaskErrors.NotFound(id);

        if (!await _authorizer.CanAccessAsync(callerId, item, AppPermissions.Tasks.Read, ct))
            return Error.Forbidden(code: "Task.Forbidden", description: "Access denied.");

        return ToResponse(item);
    }
}

public static partial class TasksEndpoints
{
    private static async Task<IResult> GetByIdAsync(
        Guid id,
        TasksModule module,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var result = await module.GetByIdAsync(user.GetCallerId(), id, ct);
        return ApiResults.ToResult(result, value => Results.Ok(value));
    }
}
