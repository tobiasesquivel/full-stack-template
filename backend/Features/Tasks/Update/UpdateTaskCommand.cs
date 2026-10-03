namespace TemplateApp.Features.Tasks.Update;

public sealed record UpdateTaskCommand(string AuthId, Guid Id, string Title);
