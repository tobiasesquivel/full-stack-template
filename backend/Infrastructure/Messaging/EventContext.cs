namespace TemplateApp.Infrastructure.Messaging;

public sealed record EventContext(Guid MessageId, string Consumer, DateTimeOffset OccurredAt, DateTimeOffset DeliveredAt);
