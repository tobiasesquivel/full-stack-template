using ErrorOr;

namespace TemplateApp.Features.Tasks;

internal static class TaskErrors
{
    public static Error NotFound(Guid taskId) => Error.NotFound(
        code: "Task.NotFound",
        description: $"Task with ID '{taskId}' not found.");

    public static readonly Error Forbidden = Error.Forbidden(
        code: "Task.Forbidden",
        description: "Access denied.");

    public static readonly Error Conflict = Error.Conflict(
        code: "Task.Conflict",
        description: "The task was changed by another request.");
}
