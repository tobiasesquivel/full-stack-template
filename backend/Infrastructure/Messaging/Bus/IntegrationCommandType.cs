namespace TemplateApp.Infrastructure.Messaging.Bus;

public sealed record IntegrationCommandType(IntegrationEventType Command);
