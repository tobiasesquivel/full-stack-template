using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Tasks.Domain;
using TemplateApp.Features.Tasks.Persistence;

namespace TemplateApp.Features.Tasks;

internal sealed class OwnerTombstones : LiveOwner.Issuer
{
    public const string Consumer = "tasks.user-deletions";
    public const int LockSpace = 2_510_001;

    private OwnerTombstones()
    {
    }

    public static int LockKey(string ownerId) =>
        BinaryPrimitives.ReadInt32BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(ownerId)));

    public static Task LockExclusiveAsync(TasksDbContext db, string ownerId, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({LockSpace}, {LockKey(ownerId)})", ct);

    public static Task BuryAsync(TasksDbContext db, string ownerId, DateTimeOffset deletedAt, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO tasks."DeletedOwners" ("OwnerId", "DeletedAt")
            VALUES ({ownerId}, {deletedAt})
            ON CONFLICT ("OwnerId") DO NOTHING
            """,
            ct);

    public static async Task<LiveOwner?> FindLiveAsync(TasksDbContext db, string ownerId, CancellationToken ct)
    {
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock_shared({LockSpace}, {LockKey(ownerId)})", ct);
        bool deleted = await db.DeletedOwners.AnyAsync(o => o.OwnerId == ownerId, ct);
        return deleted ? null : Issue(ownerId);
    }
}
