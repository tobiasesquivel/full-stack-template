using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Tests.Support;

internal static class OutboxLog
{
    public static async Task<List<OutboxRow>> OutboxRowsAsync(this IServiceProvider services, string taskId)
    {
        using IServiceScope scope = services.CreateScope();
        PlatformDbContext db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        return await db.Database
            .SqlQuery<OutboxRow>($"""
                SELECT "Id", "MessageId", "Type", "Version", "Payload"::text AS "Payload", "OccurredAt", "TraceParent",
                       "Attempts", "NextAttemptAt", "LastError", "ProcessedAt", "FailedAt"
                FROM tasks."OutboxMessages"
                WHERE "Payload"->>'taskId' = {taskId}
                ORDER BY "Id"
                """)
            .ToListAsync();
    }

    public static async Task<List<OutboxRow>> AllOutboxRowsAsync(this IServiceProvider services)
    {
        using IServiceScope scope = services.CreateScope();
        PlatformDbContext db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        return await db.Database
            .SqlQuery<OutboxRow>($"""
                SELECT "Id", "MessageId", "Type", "Version", "Payload"::text AS "Payload", "OccurredAt", "TraceParent",
                       "Attempts", "NextAttemptAt", "LastError", "ProcessedAt", "FailedAt"
                FROM tasks."OutboxMessages"
                ORDER BY "Id"
                """)
            .ToListAsync();
    }
}
