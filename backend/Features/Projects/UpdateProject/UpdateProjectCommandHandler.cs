using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Projects.Access;
using TemplateApp.Features.Projects.Contracts;
using TemplateApp.Features.Projects.Domain;
using TemplateApp.Features.Projects.Persistence;

namespace TemplateApp.Features.Projects.UpdateProject;

internal sealed partial record UpdateProjectCommand
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

        public async Task<ErrorOr<ProjectResponse>> Handle(UpdateProjectCommand command, CancellationToken ct)
        {
            Project? project = await _db.Projects.SingleOrDefaultAsync(p => p.Id == command.Id, ct);
            if (project is null)
                return ProjectErrors.NotFound(command.Id);

            if (!await _auth.CanAccessAsync(command.CallerId, project, ProjectsPermissions.Update, ct))
                return ProjectErrors.Forbidden;

            string name = Project.NormalizeName(command.Name);
            if (project.Version != command.Version)
                return project.Name == name && project.IsArchived == command.IsArchived
                    ? await _responses.ComposeAsync(ProjectRows.From(project), ct)
                    : ProjectErrors.Conflict;

            string storedKey = project.NormalizedName;
            DateTimeOffset now = _clock.GetUtcNow();
            bool changed = !command.IsArchived && project.Restore(now) == ProjectChange.Changed;

            ProjectChange renamed = project.Rename(name, now);
            if (renamed == ProjectChange.RejectedArchived)
                return ProjectErrors.Archived;

            changed |= renamed == ProjectChange.Changed;
            if (command.IsArchived)
                changed |= project.Archive(now) == ProjectChange.Changed;

            if (!changed)
                return await _responses.ComposeAsync(ProjectRows.From(project), ct);

            string ownerId = project.OwnerId;
            string key = project.NormalizedName;
            if (key != storedKey && await _db.Projects.AnyAsync(p => p.OwnerId == ownerId && p.NormalizedName == key, ct))
                return ProjectErrors.NameTaken;

            _db.Entry(project).Property(p => p.Version).OriginalValue = command.Version.Value;
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ProjectErrors.Conflict;
            }
            catch (DbUpdateException exception) when (ProjectNameUniqueness.IsViolatedBy(exception))
            {
                return ProjectErrors.NameTaken;
            }

            return await _responses.ComposeAsync(ProjectRows.From(project), ct);
        }
    }
}
