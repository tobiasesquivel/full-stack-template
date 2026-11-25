using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Tasks.Persistence;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Tasks.ListTasks;

internal sealed partial record ListTasksQuery
{
    public sealed class Handler
    {
        private readonly TasksDbContext _db;

        public Handler(TasksDbContext db)
        {
            _db = db;
        }

        public async Task<ErrorOr<TaskListResponse>> Handle(ListTasksQuery query, CancellationToken ct)
        {
            string ownerId = query.Caller.Id;
            var owned = _db.TaskItems.Where(t => t.OwnerId == ownerId);
            if (query.Cursor is not null)
            {
                KeysetCursor after = KeysetCursor.Parse(query.Cursor);
                owned = owned.Where(t => EF.Functions.LessThan(
                    ValueTuple.Create(t.CreatedAt, t.Id),
                    ValueTuple.Create(after.CreatedAt, after.Id)));
            }

            var rows = await owned
                .OrderByDescending(t => t.CreatedAt)
                .ThenByDescending(t => t.Id)
                .Take(query.PageSize + 1)
                .Select(TaskResponses.Projection)
                .AsNoTracking()
                .ToListAsync(ct);

            if (rows.Count <= query.PageSize)
                return new TaskListResponse(rows, null);

            rows.RemoveAt(query.PageSize);
            return new TaskListResponse(rows, KeysetCursor.Encode(rows[^1].CreatedAt, rows[^1].Id));
        }
    }
}
