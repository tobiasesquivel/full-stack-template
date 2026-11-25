namespace TemplateApp.Features.Projects.ListProjects;

internal sealed partial record ListProjectsQuery(string CallerId, string? Cursor, int PageSize);
