using TemplateApp.Shared.Domain;

namespace TemplateApp.Features.Tasks.Domain.Events;

internal sealed record TaskDeleted(Guid TaskId, string OwnerId, Guid? ProjectId, bool WasCompleted) : IDomainEvent;
