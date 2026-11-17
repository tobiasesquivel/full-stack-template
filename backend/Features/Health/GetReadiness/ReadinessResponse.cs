namespace TemplateApp.Features.Health.GetReadiness;

internal sealed record ReadinessResponse(string Status, DateTimeOffset Timestamp, IReadOnlyDictionary<string, string> Checks);
