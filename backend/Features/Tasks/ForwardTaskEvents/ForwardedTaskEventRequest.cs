using System.Text.Json;

namespace TemplateApp.Features.Tasks.ForwardTaskEvents;

internal sealed record ForwardedTaskEventRequest(string Type, int Version, DateTimeOffset OccurredAt, JsonElement Payload);
