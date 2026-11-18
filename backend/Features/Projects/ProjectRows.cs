using System.Linq.Expressions;
using TemplateApp.Features.Projects.Domain;

namespace TemplateApp.Features.Projects;

internal static class ProjectRows
{
    public static readonly Expression<Func<Project, ProjectRow>> Projection =
        p => new ProjectRow(p.Id, p.OwnerId, p.Name, p.State == ProjectState.Archived, p.CreatedAt, p.UpdatedAt, p.Version);

    private static readonly Func<Project, ProjectRow> Compiled = Projection.Compile();

    public static ProjectRow From(Project project) => Compiled(project);
}
