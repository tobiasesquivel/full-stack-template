namespace TemplateApp.Infrastructure.Messaging;

public sealed record IntegrationEventSubscription(IntegrationEventType Event, string Queue);
