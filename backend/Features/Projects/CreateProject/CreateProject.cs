using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Projects.Contracts;
using TemplateApp.Features.Projects.Domain;
using TemplateApp.Features.Projects.Persistence;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Projects.CreateProject;

internal sealed class CreateProject
{
    private static readonly CreateProjectRequestValidator Validator = new();

    private readonly ProjectsDbContext _db;
    private readonly IAuthModule _auth;
    private readonly TimeProvider _clock;
    private readonly ProjectResponses _responses;

    public CreateProject(ProjectsDbContext db, IAuthModule auth, TimeProvider clock, ProjectResponses responses)
    {
        _db = db;
        _auth = auth;
        _clock = clock;
        _responses = responses;
    }

    public async Task<ErrorOr<ProjectResponse>> ExecuteAsync(string callerId, CreateProjectRequest request, CancellationToken ct)
    {
        var validation = await Validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return ValidationErrors.From(validation);

        if (!await _auth.HasPermissionAsync(callerId, ProjectPermissions.Create, ct))
            return ProjectErrors.Forbidden;

        string key = Project.NormalizeKey(request.Name);
        if (await _db.Projects.AnyAsync(p => p.OwnerId == callerId && p.NormalizedName == key, ct))
            return ProjectErrors.NameTaken;

        Project project = Project.Create(callerId, request.Name, _clock.GetUtcNow());
        _db.Projects.Add(project);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (ProjectNameUniqueness.IsViolatedBy(exception))
        {
            return ProjectErrors.NameTaken;
        }

        return await _responses.ComposeNewAsync(ProjectRows.From(project), ct);
    }
}
