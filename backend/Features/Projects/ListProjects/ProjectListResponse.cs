namespace TemplateApp.Features.Projects.ListProjects;

internal sealed record ProjectListResponse(IReadOnlyList<ProjectResponse> Items, string? NextCursor);
