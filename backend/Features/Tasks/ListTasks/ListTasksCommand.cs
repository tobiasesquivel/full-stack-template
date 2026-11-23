using TemplateApp.Features.Tasks.Access;

namespace TemplateApp.Features.Tasks.ListTasks;

internal sealed record ListTasksCommand(Caller Caller, string? Cursor, int PageSize);
