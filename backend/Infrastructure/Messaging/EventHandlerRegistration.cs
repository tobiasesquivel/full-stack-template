using TemplateApp.Shared.Domain;

namespace TemplateApp.Infrastructure.Messaging;

public sealed record EventHandlerRegistration(
    Type EventType,
    string Consumer,
    Func<IServiceProvider, IDomainEvent, EventContext, CancellationToken, Task> Dispatch);
