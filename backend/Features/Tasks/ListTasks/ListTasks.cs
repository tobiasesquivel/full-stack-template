using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Tasks.Persistence;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Tasks.ListTasks;

internal sealed class ListTasks
{
    private readonly TasksDbContext _db;

    public ListTasks(TasksDbContext db)
    {
        _db = db;
    }

    public async Task<ErrorOr<TaskListResponse>> ExecuteAsync(string callerId, string? cursor, int pageSize, CancellationToken ct)
    {
        if (!KeysetCursor.IsValidPageSize(pageSize))
            return HttpErrors.InvalidPageSize;

        var owned = _db.TaskItems.Where(t => t.OwnerId == callerId);
        if (cursor is not null)
        {
            if (!KeysetCursor.TryDecode(cursor, out KeysetCursor after))
                return HttpErrors.InvalidCursor;

            owned = owned.Where(t => EF.Functions.LessThan(
                ValueTuple.Create(t.CreatedAt, t.Id),
                ValueTuple.Create(after.CreatedAt, after.Id)));
        }

        var rows = await owned
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .Take(pageSize + 1)
            .Select(TaskResponses.Projection)
            .AsNoTracking()
            .ToListAsync(ct);

        if (rows.Count <= pageSize)
            return new TaskListResponse(rows, null);

        rows.RemoveAt(pageSize);
        return new TaskListResponse(rows, KeysetCursor.Encode(rows[^1].CreatedAt, rows[^1].Id));
    }
}
