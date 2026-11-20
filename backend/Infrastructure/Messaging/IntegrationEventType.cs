namespace TemplateApp.Infrastructure.Messaging;

public sealed record IntegrationEventType(Type ClrType, string Name, int Version, string Exchange);
