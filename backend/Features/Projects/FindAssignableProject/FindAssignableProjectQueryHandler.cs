using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Projects.Persistence;

namespace TemplateApp.Features.Projects.FindAssignableProject;

internal sealed partial record FindAssignableProjectQuery
{
    public sealed class Handler
    {
        private readonly ProjectsDbContext _db;

        public Handler(ProjectsDbContext db)
        {
            _db = db;
        }

        public async Task<ErrorOr<ProjectAssignmentResponse>> Handle(FindAssignableProjectQuery query, CancellationToken ct)
        {
            ProjectRow? row = await _db.Projects.Where(p => p.Id == query.ProjectId).Select(ProjectRows.Projection).SingleOrDefaultAsync(ct);
            if (row is null || !string.Equals(row.OwnerId, query.OwnerId, StringComparison.Ordinal))
                return ProjectErrors.NotFound(query.ProjectId);

            return new ProjectAssignmentResponse(row.IsArchived);
        }
    }
}
