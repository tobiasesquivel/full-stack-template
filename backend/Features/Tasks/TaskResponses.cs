using System.Linq.Expressions;
using TemplateApp.Features.Tasks.Contracts;
using TemplateApp.Features.Tasks.Domain;

namespace TemplateApp.Features.Tasks;

public static class TaskResponses
{
    public static readonly Expression<Func<TaskItem, TaskResponse>> Projection =
        t => new TaskResponse(t.Id, t.Title, t.IsCompleted, t.CreatedAt, t.UpdatedAt);

    private static readonly Func<TaskItem, TaskResponse> Compiled = Projection.Compile();

    public static TaskResponse From(TaskItem item) => Compiled(item);
}
