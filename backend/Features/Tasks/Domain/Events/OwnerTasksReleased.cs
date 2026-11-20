using TemplateApp.Shared.Domain;

namespace TemplateApp.Features.Tasks.Domain.Events;

internal sealed record OwnerTasksReleased(Guid ClosureId, string OwnerId, DateTimeOffset ReleasedAt) : IDomainEvent;
