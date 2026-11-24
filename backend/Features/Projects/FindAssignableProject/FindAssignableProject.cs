using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Projects.Persistence;

namespace TemplateApp.Features.Projects.FindAssignableProject;

internal static class FindAssignableProject
{
    public sealed class Handler
    {
        private readonly ProjectsDbContext _db;

        public Handler(ProjectsDbContext db)
        {
            _db = db;
        }

        public async Task<ErrorOr<ProjectAssignmentResponse>> Handle(FindAssignableProjectCommand command, CancellationToken ct)
        {
            ProjectRow? row = await _db.Projects.Where(p => p.Id == command.ProjectId).Select(ProjectRows.Projection).SingleOrDefaultAsync(ct);
            if (row is null || !string.Equals(row.OwnerId, command.OwnerId, StringComparison.Ordinal))
                return ProjectErrors.NotFound(command.ProjectId);

            return new ProjectAssignmentResponse(row.IsArchived);
        }
    }
}
