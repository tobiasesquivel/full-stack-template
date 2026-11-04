using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Tasks.ListTasks;

public sealed class ListTasks
{
    private readonly AppDbContext _db;

    public ListTasks(AppDbContext db)
    {
        _db = db;
    }

    public async Task<ErrorOr<TaskListResponse>> ExecuteAsync(string callerId, int page, int pageSize, CancellationToken ct)
    {
        if (page < 1)
            return TaskErrors.InvalidPage;
        if (pageSize is < 1 or > 100)
            return TaskErrors.InvalidPageSize;

        var filtered = _db.TaskItems.Where(t => t.OwnerId == callerId);

        var totalCount = await filtered.CountAsync(ct);

        var items = await filtered
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(TaskResponses.Projection)
            .AsNoTracking()
            .ToListAsync(ct);

        return new TaskListResponse(items, totalCount, page, pageSize);
    }
}
