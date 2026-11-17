using System.Linq.Expressions;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Tasks.Contracts;
using TemplateApp.Features.Tasks.Domain;
using TemplateApp.Infrastructure.Persistence;
using TemplateApp.Shared.Domain;

namespace TemplateApp.Features.Tasks.GetTask;

public sealed class GetTask
{
    private static readonly Expression<Func<TaskItem, OwnedTask>> Read = ComposeRead((item, response) => new OwnedTask(item.OwnerId, response));

    private readonly AppDbContext _db;
    private readonly IAuthModule _auth;

    public GetTask(AppDbContext db, IAuthModule auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<ErrorOr<TaskResponse>> ExecuteAsync(string callerId, Guid id, CancellationToken ct)
    {
        OwnedTask? task = await _db.TaskItems.Where(t => t.Id == id).Select(Read).SingleOrDefaultAsync(ct);
        if (task is null)
            return TaskErrors.NotFound(id);

        if (!await _auth.CanAccessAsync(callerId, task, TaskPermissions.Read, ct))
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
