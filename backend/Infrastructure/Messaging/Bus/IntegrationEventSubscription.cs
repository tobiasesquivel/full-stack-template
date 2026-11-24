namespace TemplateApp.Infrastructure.Messaging.Bus;

public sealed record IntegrationEventSubscription(IntegrationEventType Event, string Queue);
