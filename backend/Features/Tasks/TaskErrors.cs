using ErrorOr;

namespace TemplateApp.Features.Tasks;

public static class TaskErrors
{
    public static Error NotFound(Guid taskId) => Error.NotFound(
        code: "Task.NotFound",
        description: $"Task with ID '{taskId}' not found.");
}
