using TemplateApp.Features.Tasks.Access;

namespace TemplateApp.Features.Tasks.GetTask;

internal sealed record GetTaskCommand(Caller Caller, Guid Id);
