namespace TemplateApp.Infrastructure.Messaging.Bus;

public sealed record IntegrationEventType(Type ClrType, string Name, int Version, string Exchange);
