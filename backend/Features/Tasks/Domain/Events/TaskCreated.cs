using TemplateApp.Shared.Domain;

namespace TemplateApp.Features.Tasks.Domain.Events;

internal sealed record TaskCreated(Guid TaskId, string OwnerId, Guid? ProjectId, DateTimeOffset CreatedAt) : IDomainEvent;
