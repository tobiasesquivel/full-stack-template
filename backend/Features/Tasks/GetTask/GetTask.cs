using System.Linq.Expressions;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using TemplateApp.Features.Tasks.Access;
using TemplateApp.Features.Tasks.Domain;
using TemplateApp.Features.Tasks.Persistence;
using TemplateApp.Shared.Domain;

namespace TemplateApp.Features.Tasks.GetTask;

internal static class GetTask
{
    private static readonly Expression<Func<TaskItem, OwnedTask>> Read = ComposeRead((item, response) => new OwnedTask(item.OwnerId, response));

    private static Expression<Func<TaskItem, OwnedTask>> ComposeRead(Expression<Func<TaskItem, TaskResponse, OwnedTask>> shape)
    {
        ParameterExpression item = shape.Parameters[0];
        Expression response = ReplacingExpressionVisitor.Replace(TaskResponses.Projection.Parameters[0], item, TaskResponses.Projection.Body);
        return Expression.Lambda<Func<TaskItem, OwnedTask>>(ReplacingExpressionVisitor.Replace(shape.Parameters[1], response, shape.Body), item);
    }

    public sealed class Handler
    {
        private readonly TasksDbContext _db;

        public Handler(TasksDbContext db)
        {
            _db = db;
        }

        public async Task<ErrorOr<TaskResponse>> Handle(GetTaskCommand command, CancellationToken ct)
        {
            OwnedTask? task = await _db.TaskItems.Where(t => t.Id == command.Id).Select(Read).SingleOrDefaultAsync(ct);
            if (task is null)
                return TaskErrors.NotFound(command.Id);

            if (!command.Caller.CanAccess(task, TaskPermissions.Read))
                return TaskErrors.Forbidden;

            return task.Response;
        }
    }

    private sealed record OwnedTask(string OwnerId, TaskResponse Response) : IOwnedResource;
}
