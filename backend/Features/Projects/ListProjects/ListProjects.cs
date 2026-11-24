using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Projects.Persistence;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Projects.ListProjects;

internal static class ListProjects
{
    public sealed class Handler
    {
        private readonly ProjectsDbContext _db;
        private readonly ProjectResponses _responses;

        public Handler(ProjectsDbContext db, ProjectResponses responses)
        {
            _db = db;
            _responses = responses;
        }

        public async Task<ErrorOr<ProjectListResponse>> Handle(ListProjectsCommand command, CancellationToken ct)
        {
            if (!KeysetCursor.IsValidPageSize(command.PageSize))
                return HttpErrors.InvalidPageSize;

            var owned = _db.Projects.Where(p => p.OwnerId == command.CallerId);
            if (command.Cursor is not null)
            {
                if (!KeysetCursor.TryDecode(command.Cursor, out KeysetCursor after))
                    return HttpErrors.InvalidCursor;

                owned = owned.Where(p => EF.Functions.LessThan(
                    ValueTuple.Create(p.CreatedAt, p.Id),
                    ValueTuple.Create(after.CreatedAt, after.Id)));
            }

            List<ProjectRow> rows = await owned
                .OrderByDescending(p => p.CreatedAt)
                .ThenByDescending(p => p.Id)
                .Take(command.PageSize + 1)
                .Select(ProjectRows.Projection)
                .AsNoTracking()
                .ToListAsync(ct);

            string? nextCursor = null;
            if (rows.Count > command.PageSize)
            {
                rows.RemoveAt(command.PageSize);
                nextCursor = KeysetCursor.Encode(rows[^1].CreatedAt, rows[^1].Id);
            }

            return new ProjectListResponse(await _responses.ComposeAsync(rows, ct), nextCursor);
        }
    }
}
