using TemplateApp.Shared.Domain;

namespace TemplateApp.Features.Auth.Domain.Events;

internal sealed record AccountDeleted(Guid ClosureId, string UserId, DateTimeOffset DeletedAt) : IDomainEvent;
