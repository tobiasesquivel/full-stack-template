namespace TemplateApp.Features.Projects.ApplyTaskEvent;

internal sealed record TaskEventRequest(string Type, int Version, DateTimeOffset OccurredAt, TaskEventPayload? Payload);
