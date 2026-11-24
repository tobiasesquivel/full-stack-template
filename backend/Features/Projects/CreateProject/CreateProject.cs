using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Projects.Contracts;
using TemplateApp.Features.Projects.Domain;
using TemplateApp.Features.Projects.Persistence;

namespace TemplateApp.Features.Projects.CreateProject;

internal static class CreateProject
{
    public sealed class Handler
    {
        private readonly ProjectsDbContext _db;
        private readonly IAuthModule _auth;
        private readonly TimeProvider _clock;
        private readonly ProjectResponses _responses;

        public Handler(ProjectsDbContext db, IAuthModule auth, TimeProvider clock, ProjectResponses responses)
        {
            _db = db;
            _auth = auth;
            _clock = clock;
            _responses = responses;
        }

        public async Task<ErrorOr<ProjectResponse>> Handle(CreateProjectCommand command, CancellationToken ct)
        {
            if (!await _auth.HasPermissionAsync(command.CallerId, ProjectPermissions.Create, ct))
                return ProjectErrors.Forbidden;

            string key = Project.NormalizeKey(command.Name);
            if (await _db.Projects.AnyAsync(p => p.OwnerId == command.CallerId && p.NormalizedName == key, ct))
                return ProjectErrors.NameTaken;

            Project project = Project.Create(command.CallerId, command.Name, _clock.GetUtcNow());
            _db.Projects.Add(project);
            _db.TaskCounters.Add(new ProjectTaskCounters { ProjectId = project.Id, UpdatedAt = project.CreatedAt });
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException exception) when (ProjectNameUniqueness.IsViolatedBy(exception))
            {
                return ProjectErrors.NameTaken;
            }

            return await _responses.ComposeAsync(ProjectRows.From(project), ct);
        }
    }
}
