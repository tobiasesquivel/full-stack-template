namespace TemplateApp.Features.Tasks;

internal sealed record TaskResponse(Guid Id, string Title, bool IsCompleted, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, uint Version);
