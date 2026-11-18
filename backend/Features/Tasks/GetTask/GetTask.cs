using System.Linq.Expressions;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using TemplateApp.Features.Tasks.Access;
using TemplateApp.Features.Tasks.Domain;
using TemplateApp.Features.Tasks.Persistence;
using TemplateApp.Shared.Domain;

namespace TemplateApp.Features.Tasks.GetTask;

internal sealed class GetTask
{
    private static readonly Expression<Func<TaskItem, OwnedTask>> Read = ComposeRead((item, response) => new OwnedTask(item.OwnerId, response));

    private readonly TasksDbContext _db;
    private readonly TaskAccess _access;

    public GetTask(TasksDbContext db, TaskAccess access)
    {
        _db = db;
        _access = access;
    }

    public async Task<ErrorOr<TaskResponse>> ExecuteAsync(string callerId, Guid id, CancellationToken ct)
    {
        OwnedTask? task = await _db.TaskItems.Where(t => t.Id == id).Select(Read).SingleOrDefaultAsync(ct);
        if (task is null)
            return TaskErrors.NotFound(id);

        if (!_access.CanAccess(callerId, task, TaskPermissions.Read))
            return TaskErrors.Forbidden;

        return task.Response;
    }

    private static Expression<Func<TaskItem, OwnedTask>> ComposeRead(Expression<Func<TaskItem, TaskResponse, OwnedTask>> shape)
    {
        ParameterExpression item = shape.Parameters[0];
        Expression response = ReplacingExpressionVisitor.Replace(TaskResponses.Projection.Parameters[0], item, TaskResponses.Projection.Body);
        return Expression.Lambda<Func<TaskItem, OwnedTask>>(ReplacingExpressionVisitor.Replace(shape.Parameters[1], response, shape.Body), item);
    }

    private sealed record OwnedTask(string OwnerId, TaskResponse Response) : IOwnedResource;
}
