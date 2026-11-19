using ErrorOr;

namespace TemplateApp.Features.Projects;

internal static class ProjectErrors
{
    public static Error NotFound(Guid projectId) => Error.NotFound(
        code: "Project.NotFound",
        description: $"Project with ID '{projectId}' not found.");

    public static readonly Error Forbidden = Error.Forbidden(
        code: "Project.Forbidden",
        description: "Access denied.");

    public static readonly Error Conflict = Error.Conflict(
        code: "Project.Conflict",
        description: "The project was changed by another request.");

    public static readonly Error NameTaken = Error.Conflict(
        code: "Project.NameTaken",
        description: "You already have a project with this name.");

    public static readonly Error Archived = Error.Conflict(
        code: "Project.Archived",
        description: "An archived project cannot be renamed.");

    public static readonly Error TaskCountersRebuilding = Error.Custom(
        type: 503,
        code: "Project.TaskCountersRebuilding",
        description: "The task counters are being rebuilt. Try again later.");
}
