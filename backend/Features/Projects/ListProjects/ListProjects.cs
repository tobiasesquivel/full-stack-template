using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Projects.Persistence;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Projects.ListProjects;

internal sealed class ListProjects
{
    private readonly ProjectsDbContext _db;
    private readonly ProjectResponses _responses;

    public ListProjects(ProjectsDbContext db, ProjectResponses responses)
    {
        _db = db;
        _responses = responses;
    }

    public async Task<ErrorOr<ProjectListResponse>> ExecuteAsync(string callerId, string? cursor, int pageSize, CancellationToken ct)
    {
        if (!KeysetCursor.IsValidPageSize(pageSize))
            return HttpErrors.InvalidPageSize;

        var owned = _db.Projects.Where(p => p.OwnerId == callerId);
        if (cursor is not null)
        {
            if (!KeysetCursor.TryDecode(cursor, out KeysetCursor after))
                return HttpErrors.InvalidCursor;

            owned = owned.Where(p => EF.Functions.LessThan(
                ValueTuple.Create(p.CreatedAt, p.Id),
                ValueTuple.Create(after.CreatedAt, after.Id)));
        }

        List<ProjectRow> rows = await owned
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Take(pageSize + 1)
            .Select(ProjectRows.Projection)
            .AsNoTracking()
            .ToListAsync(ct);

        string? nextCursor = null;
        if (rows.Count > pageSize)
        {
            rows.RemoveAt(pageSize);
            nextCursor = KeysetCursor.Encode(rows[^1].CreatedAt, rows[^1].Id);
        }

        return new ProjectListResponse(await _responses.ComposeAsync(rows, ct), nextCursor);
    }
}
