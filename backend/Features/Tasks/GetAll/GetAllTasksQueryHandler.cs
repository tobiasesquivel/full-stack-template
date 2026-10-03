using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Tasks.Common;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Tasks.GetAll;

public sealed class GetAllTasksQueryHandler(AppDbContext _db)
{
    public async Task<ErrorOr<GetAllTasksQueryResult>> Handle(GetAllTasksQuery query, CancellationToken ct)
    {
        IReadOnlyList<TaskItemResponse> tasks = await _db.TaskItems
            .Where(t => t.AuthId == query.AuthId)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new TaskItemResponse(t.Id, t.Title, t.IsCompleted, t.CreatedAt))
            .AsNoTracking()
            .ToListAsync(ct);

        return new GetAllTasksQueryResult(tasks);
    }
}
