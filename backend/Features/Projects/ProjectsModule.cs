using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Projects.Contracts;
using TemplateApp.Features.Projects.Persistence;

namespace TemplateApp.Features.Projects;

internal sealed class ProjectsModule : IProjectsModule
{
    private readonly ProjectsDbContext _db;

    public ProjectsModule(ProjectsDbContext db)
    {
        _db = db;
    }

    public async Task<ProjectAssignmentInfo?> FindAssignableProjectAsync(string callerId, Guid projectId, CancellationToken ct)
    {
        ProjectRow? row = await _db.Projects.Where(p => p.Id == projectId).Select(ProjectRows.Projection).SingleOrDefaultAsync(ct);
        return row is null || !string.Equals(row.OwnerId, callerId, StringComparison.Ordinal)
            ? null
            : new ProjectAssignmentInfo(row.IsArchived);
    }

    public async Task DeleteOwnedProjectsAsync(string ownerId, CancellationToken ct)
    {
        _db.Projects.RemoveRange(await _db.Projects.Where(p => p.OwnerId == ownerId).ToListAsync(ct));
        await _db.SaveChangesAsync(ct);
    }
}
