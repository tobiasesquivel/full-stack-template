namespace TemplateApp.Features.Health;

public sealed record HealthStatusResponse(string Status, DateTimeOffset Timestamp);
