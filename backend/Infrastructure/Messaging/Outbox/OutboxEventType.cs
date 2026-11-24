namespace TemplateApp.Infrastructure.Messaging.Outbox;

public sealed record OutboxEventType(Type ClrType, string Name, int Version);
