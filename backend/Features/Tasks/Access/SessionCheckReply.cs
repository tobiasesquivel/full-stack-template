namespace TemplateApp.Features.Tasks.Access;

internal sealed record SessionCheckReply(IReadOnlyList<string> Permissions);
