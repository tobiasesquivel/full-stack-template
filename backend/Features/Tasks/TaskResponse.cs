namespace TemplateApp.Features.Tasks;

public sealed record TaskResponse(Guid Id, string Title, bool IsCompleted, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, uint Version);
