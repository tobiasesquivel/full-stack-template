namespace TemplateApp.Features.Tasks.Contracts;

public sealed record TaskResponse(Guid Id, string Title, bool IsCompleted, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);
