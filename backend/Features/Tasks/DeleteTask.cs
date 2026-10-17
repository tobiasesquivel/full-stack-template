using System.Security.Claims;
using ErrorOr;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Tasks.Contracts;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Tasks;

public sealed partial class TasksModule
{
    public async Task<ErrorOr<Success>> DeleteAsync(string callerId, Guid id, CancellationToken ct)
    {
        var item = await _db.TaskItems.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (item is null)
            return TaskErrors.NotFound(id);

        if (!await _access.CanAccessAsync(callerId, item, TaskPermissions.Delete, ct))
            return Error.Forbidden(code: "Task.Forbidden", description: "Access denied.");

        _db.TaskItems.Remove(item);
        await _db.SaveChangesAsync(ct);

        return Result.Success;
    }
}

public static partial class TasksEndpoints
{
    internal static async Task<IResult> DeleteAsync(
        Guid id,
        TasksModule module,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        ErrorOr<Success> result = await module.DeleteAsync(user.GetCallerId(), id, ct);
        return ApiResults.ToResult(result, _ => Results.NoContent());
    }
}
