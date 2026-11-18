using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Projects.Contracts;
using TemplateApp.Features.Projects.Persistence;

namespace TemplateApp.Features.Projects.GetProject;

internal sealed class GetProject
{
    private readonly ProjectsDbContext _db;
    private readonly IAuthModule _auth;
    private readonly ProjectResponses _responses;

    public GetProject(ProjectsDbContext db, IAuthModule auth, ProjectResponses responses)
    {
        _db = db;
        _auth = auth;
        _responses = responses;
    }

    public async Task<ErrorOr<ProjectResponse>> ExecuteAsync(string callerId, Guid id, CancellationToken ct)
    {
        ProjectRow? row = await _db.Projects.Where(p => p.Id == id).Select(ProjectRows.Projection).SingleOrDefaultAsync(ct);
        if (row is null)
            return ProjectErrors.NotFound(id);

        if (!await _auth.CanAccessAsync(callerId, row, ProjectPermissions.Read, ct))
            return ProjectErrors.Forbidden;

        return await _responses.ComposeAsync(row, ct);
    }
}
