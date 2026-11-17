using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Tests.Support;

internal static class AuditLog
{
    public static async Task<List<AuditRow>> AuditEntriesAsync(this IServiceProvider services, string entityId)
    {
        using IServiceScope scope = services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Database
            .SqlQuery<AuditRow>($"""
                SELECT "Id", "OccurredAt", "ActorKind", "ActorId", "EntityType", "EntityId", "Operation",
                       array_to_string("ChangedProperties", ',') AS "ChangedProperties", "TraceId"
                FROM "AuditEntries"
                WHERE "EntityId" = {entityId}
                ORDER BY "Id"
                """)
            .ToListAsync();
    }

    public static async Task<List<AuditRow>> AuditEntriesByActorAsync(this IServiceProvider services, string actorId)
    {
        using IServiceScope scope = services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Database
            .SqlQuery<AuditRow>($"""
                SELECT "Id", "OccurredAt", "ActorKind", "ActorId", "EntityType", "EntityId", "Operation",
                       array_to_string("ChangedProperties", ',') AS "ChangedProperties", "TraceId"
                FROM "AuditEntries"
                WHERE "ActorId" = {actorId}
                ORDER BY "Id"
                """)
            .ToListAsync();
    }

    public static async Task<string> AuditDumpAsync(this IServiceProvider services, string actorId, params string[] entityIds)
    {
        using IServiceScope scope = services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        List<string> rows = await db.Database
            .SqlQuery<string>($"""
                SELECT row_to_json(a)::text AS "Value"
                FROM "AuditEntries" a
                WHERE a."ActorId" = {actorId} OR a."EntityId" = ANY({entityIds})
                """)
            .ToListAsync();
        return string.Join('\n', rows);
    }
}
