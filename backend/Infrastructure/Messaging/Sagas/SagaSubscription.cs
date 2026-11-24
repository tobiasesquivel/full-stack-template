using TemplateApp.Infrastructure.Messaging.Bus;

namespace TemplateApp.Infrastructure.Messaging.Sagas;

public sealed record SagaSubscription(IntegrationEventType Message, string Queue);
