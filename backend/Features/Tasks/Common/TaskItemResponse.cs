namespace TemplateApp.Features.Tasks.Common;

public sealed record TaskItemResponse(Guid Id, string Title, bool IsCompleted, DateTime CreatedAt);
