namespace TemplateApp.Features.Auth.Contracts;

public sealed record AccountDeletedInfo(Guid ClosureId, string UserId, DateTimeOffset DeletedAt);
