namespace TemplateApp.Features.Tasks.Delete;

public sealed record DeleteTaskCommand(string AuthId, Guid Id);
