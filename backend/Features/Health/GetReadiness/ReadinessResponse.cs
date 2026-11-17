namespace TemplateApp.Features.Health.GetReadiness;

public sealed record ReadinessResponse(string Status, DateTimeOffset Timestamp, IReadOnlyDictionary<string, string> Checks);
