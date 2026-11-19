using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Projects.Persistence;

namespace TemplateApp.Features.Projects;

internal static class TaskCountersProjection
{
    public const string Consumer = "projects.task-counters";

    private const long LockKey = 7_164_253_024;

    public static async Task<bool> TryEnterSharedAsync(ProjectsDbContext db, CancellationToken ct) =>
        await db.Database
            .SqlQuery<bool>($"""SELECT pg_try_advisory_xact_lock_shared({LockKey}) AS "Value" """)
            .SingleAsync(ct);

    public static Task EnterExclusiveAsync(ProjectsDbContext db, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({LockKey})", ct);
}
