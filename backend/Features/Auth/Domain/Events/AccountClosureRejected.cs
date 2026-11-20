using TemplateApp.Shared.Domain;

namespace TemplateApp.Features.Auth.Domain.Events;

internal sealed record AccountClosureRejected(Guid ClosureId, string UserId, string Reason, DateTimeOffset RejectedAt) : IDomainEvent;
