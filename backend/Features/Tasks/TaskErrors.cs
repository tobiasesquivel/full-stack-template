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

    public static readonly Error InvalidPage = Error.Validation(
        code: "Page",
        description: "Page must be greater than or equal to 1.");

    public static readonly Error InvalidPageSize = Error.Validation(
        code: "PageSize",
        description: "PageSize must be between 1 and 100.");
}
