using ErrorOr;

namespace TemplateApp.Features.Tasks;

public static class TaskErrors
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

    public static readonly Error InvalidCursor = Error.Validation(
        code: "Cursor",
        description: "Cursor is invalid.");

    public static readonly Error InvalidPageSize = Error.Validation(
        code: "PageSize",
        description: "PageSize must be between 1 and 100.");
}
