using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Tests.Support;

internal static class TaskCounterRows
{
    public static async Task<(int Tasks, int Completed)?> TaskCountersAsync(this IServiceProvider services, string projectId)
    {
        using IServiceScope scope = services.CreateScope();
        PlatformDbContext db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        List<TaskCounterRow> rows = await db.Database
            .SqlQuery<TaskCounterRow>($"""
                SELECT "ProjectId", "TaskCount", "CompletedTaskCount"
                FROM projects."ProjectTaskCounters"
                WHERE "ProjectId" = {Guid.Parse(projectId)}
                """)
            .ToListAsync();
        return rows is [TaskCounterRow row] ? (row.TaskCount, row.CompletedTaskCount) : null;
    }

    public static async Task<List<TaskCounterRow>> AllTaskCountersAsync(this IServiceProvider services)
    {
        using IServiceScope scope = services.CreateScope();
        PlatformDbContext db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        return await db.Database
            .SqlQuery<TaskCounterRow>($"""
                SELECT "ProjectId", "TaskCount", "CompletedTaskCount"
                FROM projects."ProjectTaskCounters"
                ORDER BY "ProjectId"
                """)
            .ToListAsync();
    }

    public static async Task<List<TaskCounterRow>> CountedTasksAsync(this IServiceProvider services)
    {
        using IServiceScope scope = services.CreateScope();
        PlatformDbContext db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        return await db.Database
            .SqlQuery<TaskCounterRow>($"""
                SELECT p."Id" AS "ProjectId",
                       count(t."Id")::int AS "TaskCount",
                       count(t."CompletedAt")::int AS "CompletedTaskCount"
                FROM projects."Projects" AS p
                LEFT JOIN tasks."TaskItems" AS t ON t."ProjectId" = p."Id"
                GROUP BY p."Id"
                ORDER BY p."Id"
                """)
            .ToListAsync();
    }
}
