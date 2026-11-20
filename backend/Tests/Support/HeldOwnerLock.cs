using Npgsql;
using TemplateApp.Features.Tasks;
using Xunit;

namespace TemplateApp.Tests.Support;

internal sealed class HeldOwnerLock : IAsyncDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;
    private readonly long _ownerKey;

    private HeldOwnerLock(NpgsqlConnection connection, NpgsqlTransaction transaction, long ownerKey)
    {
        _connection = connection;
        _transaction = transaction;
        _ownerKey = ownerKey;
    }

    public static Task<HeldOwnerLock> ExclusiveAsync(string database, string ownerId) =>
        TakeAsync(database, ownerId, "pg_advisory_xact_lock");

    public static Task<HeldOwnerLock> SharedAsync(string database, string ownerId) =>
        TakeAsync(database, ownerId, "pg_advisory_xact_lock_shared");

    public async Task ExecuteAsync(string sql, params object[] values)
    {
        await using NpgsqlCommand command = new(sql, _connection, _transaction);
        foreach (object value in values)
            command.Parameters.Add(new NpgsqlParameter { Value = value });
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    public Task<bool> BlocksAsync(Task competitor) => WaitsAsync(_connection, _transaction, _ownerKey, competitor);

    public static async Task<bool> SomeoneWaitsAsync(string database, string ownerId, Task competitor)
    {
        await using NpgsqlConnection connection = await TestDatabases.OpenAsync(database);
        return await WaitsAsync(connection, null, (uint)OwnerTombstones.LockKey(ownerId), competitor);
    }

    private static async Task<bool> WaitsAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, long ownerKey, Task competitor)
    {
        DateTime deadline = DateTime.UtcNow + Wait;
        while (!competitor.IsCompleted && DateTime.UtcNow < deadline)
        {
            await using NpgsqlCommand waiting = new(
                """
                SELECT count(*) FROM pg_locks
                WHERE locktype = 'advisory' AND NOT granted
                  AND classid::bigint = $1 AND objid::bigint = $2 AND objsubid = 2
                """,
                connection,
                transaction);
            waiting.Parameters.Add(new NpgsqlParameter { Value = (long)OwnerTombstones.LockSpace });
            waiting.Parameters.Add(new NpgsqlParameter { Value = ownerKey });
            if ((long)(await waiting.ExecuteScalarAsync(TestContext.Current.CancellationToken))! > 0)
                return true;

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        return false;
    }

    public Task CommitAsync() => _transaction.CommitAsync(TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _transaction.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static async Task<HeldOwnerLock> TakeAsync(string database, string ownerId, string function)
    {
        NpgsqlConnection connection = await TestDatabases.OpenAsync(database);
        NpgsqlTransaction transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        int key = OwnerTombstones.LockKey(ownerId);
        await using (NpgsqlCommand take = new($"SELECT {function}($1, $2)", connection, transaction))
        {
            take.Parameters.Add(new NpgsqlParameter { Value = OwnerTombstones.LockSpace });
            take.Parameters.Add(new NpgsqlParameter { Value = key });
            await take.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        return new HeldOwnerLock(connection, transaction, (uint)key);
    }
}
