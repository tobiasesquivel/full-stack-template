using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Projects.Access;
using TemplateApp.Features.Projects.Contracts;
using TemplateApp.Features.Projects.Persistence;

namespace TemplateApp.Features.Projects.GetProject;

internal sealed partial record GetProjectQuery
{
    public sealed class Handler
    {
        private readonly ProjectsDbContext _db;
        private readonly IAuthModule _auth;
        private readonly ProjectResponses _responses;

        public Handler(ProjectsDbContext db, IAuthModule auth, ProjectResponses responses)
        {
            _db = db;
            _auth = auth;
            _responses = responses;
        }

        public async Task<ErrorOr<ProjectResponse>> Handle(GetProjectQuery query, CancellationToken ct)
        {
            ProjectRow? row = await _db.Projects.Where(p => p.Id == query.Id).Select(ProjectRows.Projection).SingleOrDefaultAsync(ct);
            if (row is null)
                return ProjectErrors.NotFound(query.Id);

            if (!await _auth.CanAccessAsync(query.CallerId, row, ProjectsPermissions.Read, ct))
                return ProjectErrors.Forbidden;

            return await _responses.ComposeAsync(row, ct);
        }
    }
}
