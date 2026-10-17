namespace TemplateApp.Features.Health.Contracts;

public sealed record HealthStatusResponse(string Status, DateTimeOffset Timestamp);
