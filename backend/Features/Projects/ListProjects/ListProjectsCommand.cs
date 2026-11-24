namespace TemplateApp.Features.Projects.ListProjects;

internal sealed record ListProjectsCommand(string CallerId, string? Cursor, int PageSize);
