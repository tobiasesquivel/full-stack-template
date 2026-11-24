using TemplateApp.Features.Tasks.Access;

namespace TemplateApp.Features.Tasks.UpdateTask;

internal sealed record UpdateTaskCommand(Caller Caller, Guid Id, string Title, bool IsCompleted, uint? Version);
