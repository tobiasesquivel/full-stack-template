using System.Security.Claims;
using ErrorOr;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Tasks.Contracts;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Tasks;

public sealed partial class TasksModule
{
    public async Task<ErrorOr<TaskListResponse>> ListAsync(string callerId, int page, int pageSize, CancellationToken ct)
    {
        if (page < 1)
            return Error.Validation(code: "Page", description: "Page must be greater than or equal to 1.");
        if (pageSize is < 1 or > 100)
            return Error.Validation(code: "PageSize", description: "PageSize must be between 1 and 100.");

        var filtered = _db.TaskItems.Where(t => t.OwnerId == callerId);

        var totalCount = await filtered.CountAsync(ct);

        var items = await filtered
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new TaskResponse(t.Id, t.Title, t.IsCompleted, t.CreatedAt, t.UpdatedAt))
            .AsNoTracking()
            .ToListAsync(ct);

        return new TaskListResponse(items, totalCount, page, pageSize);
    }
}

public static partial class TasksEndpoints
{
    private static async Task<IResult> ListAsync(
        TasksModule module,
        ClaimsPrincipal user,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await module.ListAsync(user.GetCallerId(), page, pageSize, ct);
        return ApiResults.ToResult(result, value => Results.Ok(value));
    }
}
