namespace TemplateApp.Features.Health.GetLiveness;

internal sealed record HealthStatusResponse(string Status, DateTimeOffset Timestamp);
