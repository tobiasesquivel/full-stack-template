using TemplateApp.Shared.Domain;

namespace TemplateApp.Features.Tasks.Domain.Events;

internal sealed record TaskCompleted(Guid TaskId, string OwnerId, Guid? ProjectId, DateTimeOffset CompletedAt) : IDomainEvent;
