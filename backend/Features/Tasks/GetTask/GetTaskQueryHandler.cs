using System.Linq.Expressions;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using TemplateApp.Features.Tasks.Access;
using TemplateApp.Features.Tasks.Domain;
using TemplateApp.Features.Tasks.Persistence;
using TemplateApp.Shared.Domain;

namespace TemplateApp.Features.Tasks.GetTask;

internal sealed partial record GetTaskQuery
{
    public sealed class Handler
    {
        private static readonly Expression<Func<TaskItem, OwnedTask>> Read = ComposeRead((item, response) => new OwnedTask(item.OwnerId, response));

        private readonly TasksDbContext _db;

        public Handler(TasksDbContext db)
        {
            _db = db;
        }

        public async Task<ErrorOr<TaskResponse>> Handle(GetTaskQuery query, CancellationToken ct)
        {
            OwnedTask? task = await _db.TaskItems.Where(t => t.Id == query.Id).Select(Read).SingleOrDefaultAsync(ct);
            if (task is null)
                return TaskErrors.NotFound(query.Id);

            if (!query.Caller.CanAccess(task, TasksPermissions.Read))
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
}
