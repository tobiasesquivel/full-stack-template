using TemplateApp.Shared.Domain;

namespace TemplateApp.Features.Tasks.Domain.Events;

internal sealed record OwnerTasksHeld(Guid ClosureId, string OwnerId, DateTimeOffset HeldAt) : IDomainEvent;
