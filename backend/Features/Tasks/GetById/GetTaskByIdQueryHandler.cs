using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Tasks.Common;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Tasks.GetById;

public sealed class GetTaskByIdQueryHandler(AppDbContext _db)
{
    public async Task<ErrorOr<TaskItemResponse>> Handle(GetTaskByIdQuery query, CancellationToken ct)
    {
        TaskItemResponse? response = await _db.TaskItems
            .Where(t => t.Id == query.Id && t.AuthId == query.AuthId)
            .Select(t => new TaskItemResponse(t.Id, t.Title, t.IsCompleted, t.CreatedAt))
            .AsNoTracking()
            .SingleOrDefaultAsync(ct);

        if (response is null)
            return TaskErrors.NotFound(query.Id);

        return response;
    }
}
