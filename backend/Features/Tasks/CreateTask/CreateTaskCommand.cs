using TemplateApp.Features.Tasks.Access;

namespace TemplateApp.Features.Tasks.CreateTask;

internal sealed partial record CreateTaskCommand(Caller Caller, string Title, Guid? ProjectId);
