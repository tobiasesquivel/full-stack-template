using TemplateApp.Features.Tasks.Access;

namespace TemplateApp.Features.Tasks.DeleteTask;

internal sealed partial record DeleteTaskCommand(Caller Caller, Guid Id);
