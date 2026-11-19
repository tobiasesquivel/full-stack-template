namespace TemplateApp.Features.Projects.ApplyTaskEvent;

internal sealed record TaskEventPayload(Guid? ProjectId, bool WasCompleted);
