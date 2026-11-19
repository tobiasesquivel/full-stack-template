using TemplateApp.Shared.Domain;

namespace TemplateApp.Features.Tasks.Domain.Events;

internal sealed record TaskReopened(Guid TaskId, string OwnerId, Guid? ProjectId, DateTimeOffset ReopenedAt) : IDomainEvent;
