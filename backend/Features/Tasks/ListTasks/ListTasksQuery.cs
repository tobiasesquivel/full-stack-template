using TemplateApp.Features.Tasks.Access;

namespace TemplateApp.Features.Tasks.ListTasks;

internal sealed partial record ListTasksQuery(Caller Caller, string? Cursor, int PageSize);
