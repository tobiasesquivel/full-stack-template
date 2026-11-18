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

    public static readonly Error ProjectNotAssignable = Error.Validation(
        code: "ProjectId",
        description: "ProjectId must be one of your projects.");

    public static readonly Error ProjectArchived = Error.Conflict(
        code: "Task.ProjectArchived",
        description: "A task cannot be added to an archived project.");
}
