namespace TemplateApp.Infrastructure.Messaging.Mediator;

public sealed record MediatorHandlerType(Type ClrType, MediatorTransaction Transaction);
