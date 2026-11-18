using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Projects.Persistence;

namespace TemplateApp.Features.Projects.FindAssignableProject;

internal sealed class FindAssignableProject
{
    private readonly ProjectsDbContext _db;

    public FindAssignableProject(ProjectsDbContext db)
    {
        _db = db;
    }

    public async Task<ErrorOr<ProjectAssignmentResponse>> ExecuteAsync(string ownerId, Guid projectId, CancellationToken ct)
    {
        ProjectRow? row = await _db.Projects.Where(p => p.Id == projectId).Select(ProjectRows.Projection).SingleOrDefaultAsync(ct);
        if (row is null || !string.Equals(row.OwnerId, ownerId, StringComparison.Ordinal))
            return ProjectErrors.NotFound(projectId);

        return new ProjectAssignmentResponse(row.IsArchived);
    }
}
