namespace TemplateApp.Infrastructure.Messaging;

public sealed record OutboxEventType(Type ClrType, string Name, int Version);
