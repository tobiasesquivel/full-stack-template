using TemplateApp.Shared.Domain;

namespace TemplateApp.Features.Users.Domain.Events;

internal sealed record AccountClosureRequested(Guid ClosureId, string AuthId, DateTimeOffset RequestedAt) : IDomainEvent;
