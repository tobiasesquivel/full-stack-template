using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Tasks.Persistence;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Tasks.ListTasks;

internal static class ListTasks
{
    public sealed class Handler
    {
        private readonly TasksDbContext _db;

        public Handler(TasksDbContext db)
        {
            _db = db;
        }

        public async Task<ErrorOr<TaskListResponse>> Handle(ListTasksCommand command, CancellationToken ct)
        {
            if (!KeysetCursor.IsValidPageSize(command.PageSize))
                return HttpErrors.InvalidPageSize;

            string ownerId = command.Caller.Id;
            var owned = _db.TaskItems.Where(t => t.OwnerId == ownerId);
            if (command.Cursor is not null)
            {
                if (!KeysetCursor.TryDecode(command.Cursor, out KeysetCursor after))
                    return HttpErrors.InvalidCursor;

                owned = owned.Where(t => EF.Functions.LessThan(
                    ValueTuple.Create(t.CreatedAt, t.Id),
                    ValueTuple.Create(after.CreatedAt, after.Id)));
            }

            var rows = await owned
                .OrderByDescending(t => t.CreatedAt)
                .ThenByDescending(t => t.Id)
                .Take(command.PageSize + 1)
                .Select(TaskResponses.Projection)
                .AsNoTracking()
                .ToListAsync(ct);

            if (rows.Count <= command.PageSize)
                return new TaskListResponse(rows, null);

            rows.RemoveAt(command.PageSize);
            return new TaskListResponse(rows, KeysetCursor.Encode(rows[^1].CreatedAt, rows[^1].Id));
        }
    }
}
