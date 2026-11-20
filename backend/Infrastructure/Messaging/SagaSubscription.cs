namespace TemplateApp.Infrastructure.Messaging;

public sealed record SagaSubscription(IntegrationEventType Message, string Queue);
