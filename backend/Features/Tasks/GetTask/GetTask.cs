using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Tasks.Contracts;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Tasks.GetTask;

public sealed class GetTask
{
    private readonly AppDbContext _db;
    private readonly IAccessPolicy _access;

    public GetTask(AppDbContext db, IAccessPolicy access)
    {
        _db = db;
        _access = access;
    }

    public async Task<ErrorOr<TaskResponse>> ExecuteAsync(string callerId, Guid id, CancellationToken ct)
    {
        var item = await _db.TaskItems.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (item is null)
            return TaskErrors.NotFound(id);

        if (!await _access.CanAccessAsync(callerId, item, TaskPermissions.Read, ct))
            return TaskErrors.Forbidden;

        return TaskResponses.From(item);
    }
}
