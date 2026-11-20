using TemplateApp.Shared.Domain;

namespace TemplateApp.Features.Users.Domain.Events;

internal sealed record UserDeleted(string AuthId, DateTimeOffset DeletedAt) : IDomainEvent;
