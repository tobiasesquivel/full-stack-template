using TemplateApp.Features.Tasks.Access;

namespace TemplateApp.Features.Tasks.DeleteTask;

internal sealed record DeleteTaskCommand(Caller Caller, Guid Id);
