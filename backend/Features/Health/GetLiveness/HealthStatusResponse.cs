namespace TemplateApp.Features.Health.GetLiveness;

public sealed record HealthStatusResponse(string Status, DateTimeOffset Timestamp);
