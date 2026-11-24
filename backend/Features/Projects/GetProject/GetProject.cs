using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Projects.Contracts;
using TemplateApp.Features.Projects.Persistence;

namespace TemplateApp.Features.Projects.GetProject;

internal static class GetProject
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

        public async Task<ErrorOr<ProjectResponse>> Handle(GetProjectCommand command, CancellationToken ct)
        {
            ProjectRow? row = await _db.Projects.Where(p => p.Id == command.Id).Select(ProjectRows.Projection).SingleOrDefaultAsync(ct);
            if (row is null)
                return ProjectErrors.NotFound(command.Id);

            if (!await _auth.CanAccessAsync(command.CallerId, row, ProjectPermissions.Read, ct))
                return ProjectErrors.Forbidden;

            return await _responses.ComposeAsync(row, ct);
        }
    }
}
