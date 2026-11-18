using System.Linq.Expressions;
using TemplateApp.Features.Tasks.Domain;

namespace TemplateApp.Features.Tasks;

internal static class TaskResponses
{
    public static readonly Expression<Func<TaskItem, TaskResponse>> Projection =
        t => new TaskResponse(t.Id, t.Title, t.CompletedAt != null, t.CreatedAt, t.UpdatedAt, t.Version, t.ProjectId);

    private static readonly Func<TaskItem, TaskResponse> Compiled = Projection.Compile();

    public static TaskResponse From(TaskItem item) => Compiled(item);
}
