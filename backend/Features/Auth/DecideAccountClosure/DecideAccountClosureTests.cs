using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TemplateApp.Features.Auth.Domain;
using TemplateApp.Features.Auth.Persistence;
using TemplateApp.Infrastructure.Messaging;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Auth.DecideAccountClosure;

public sealed class DecideAccountClosureTests : IDisposable
{
    private readonly string _server;
    private readonly Uri _broker;
    private readonly List<IsolatedApi> _apis = [];

    public DecideAccountClosureTests(PostgresFixture postgres, RabbitMqFixture rabbit)
    {
        _server = postgres.ConnectionString;
        _broker = rabbit.Uri;
    }

    [Fact]
    public async Task TwoSuperAdminsClosingAtOnce_OneAccountIsDeleted_AndTheOtherClosureIsRejected()
    {
        IsolatedApi api = await StartAsync();
        TestSession alice = await api.SignUpAsync("alice");
        TestSession bob = await api.SignUpAsync("bob");
        await api.Pair.Monolith.Services.SetRoleAsync(alice.UserName, "SuperAdmin", member: true);
        await api.Pair.Monolith.Services.SetRoleAsync(bob.UserName, "SuperAdmin", member: true);
        Guid aliceClosure = Guid.CreateVersion7();
        Guid bobClosure = Guid.CreateVersion7();
        CancellationToken ct = TestContext.Current.CancellationToken;

        await using NpgsqlConnection held = await TestDatabases.OpenAsync(api.Database);
        await using NpgsqlTransaction holding = await held.BeginTransactionAsync(ct);
        await using (NpgsqlCommand take = new("SELECT pg_advisory_xact_lock($1, $2)", held, holding))
        {
            take.Parameters.Add(new NpgsqlParameter { Value = DecideAccountClosure.LockSpace });
            take.Parameters.Add(new NpgsqlParameter { Value = DecideAccountClosure.SuperAdminsLockKey });
            await take.ExecuteNonQueryAsync(ct);
        }

        Task first = DecideAsync(api, aliceClosure, alice.AuthId);
        Task second = DecideAsync(api, bobClosure, bob.AuthId);
        long waiting = await Eventually.Async(() => WaitingAsync(held, holding), count => count == 2, TimeSpan.FromSeconds(20));
        await holding.CommitAsync(ct);
        await Task.WhenAll(first, second);

        Assert.Equal(2, waiting);
        string?[] outcomes = [await DecisionAsync(api, aliceClosure), await DecisionAsync(api, bobClosure)];
        Assert.Equal(["Completed", "Rejected"], outcomes.Order(StringComparer.Ordinal));
        long accountsLeft = (await api.Pair.Monolith.Services.OwnedRowsAsync(alice.AuthId)).Accounts + (await api.Pair.Monolith.Services.OwnedRowsAsync(bob.AuthId)).Accounts;
        Assert.Equal(1, accountsLeft);
    }

    [Fact]
    public async Task AFailureAfterIdentitySaved_RollsBackTheDeletionTheDecisionTheInboxAndTheOutboxTogether()
    {
        FailAfterSaving<AccountClosure> fault = new(EntityState.Added, error: () => new NpgsqlException("The connection dropped after saving (test fault)."));
        IsolatedApi api = await StartAsync(services => services.ConfigureDbContext<AuthDbContext>((_, options) => options.AddInterceptors(fault)));
        TestSession bob = await api.SignUpAsync("bob");
        Guid closure = Guid.CreateVersion7();

        await Assert.ThrowsAsync<NpgsqlException>(() => DecideAsync(api, closure, bob.AuthId));

        Assert.Equal(1, fault.Failures);
        Assert.Equal(new OwnedRows(1, 1, 1, 1, 0, 0, 0), await api.Pair.Monolith.Services.OwnedRowsAsync(bob.AuthId));
        Assert.Null(await DecisionAsync(api, closure));
        Assert.Equal(0, await api.Pair.Monolith.Services.CountAsync($"""SELECT count(*) AS "Value" FROM auth."OutboxMessages" """));
        Assert.Equal(0, await api.Pair.Monolith.Services.CountAsync($"""SELECT count(*) AS "Value" FROM auth."InboxMessages" """));
    }

    public void Dispose()
    {
        foreach (IsolatedApi api in _apis)
            api.Dispose();
    }

    private static async Task DecideAsync(IsolatedApi api, Guid closureId, string userId)
    {
        await using AsyncServiceScope scope = api.Pair.Monolith.Services.CreateAsyncScope();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await scope.ServiceProvider.GetRequiredService<DecideAccountClosure>()
            .ExecuteAsync(closureId, userId, new EventContext(Guid.NewGuid(), DecideAccountClosure.Consumer, now, now), TestContext.Current.CancellationToken);
    }

    private static Task<string?> DecisionAsync(IsolatedApi api, Guid closureId) =>
        api.Pair.Monolith.Services.TextAsync($"""SELECT "Outcome" AS "Value" FROM auth."AccountClosures" WHERE "ClosureId" = {closureId}""");

    private static async Task<long> WaitingAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        await using NpgsqlCommand waiting = new(
            """
            SELECT count(*) FROM pg_locks
            WHERE locktype = 'advisory' AND NOT granted
              AND classid::bigint = $1 AND objid::bigint = $2 AND objsubid = 2
            """,
            connection,
            transaction);
        waiting.Parameters.Add(new NpgsqlParameter { Value = (long)DecideAccountClosure.LockSpace });
        waiting.Parameters.Add(new NpgsqlParameter { Value = (long)DecideAccountClosure.SuperAdminsLockKey });
        return (long)(await waiting.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private async Task<IsolatedApi> StartAsync(Action<IServiceCollection>? configure = null)
    {
        IsolatedApi api = await IsolatedApi.StartAsync(_server, _broker, configure);
        _apis.Add(api);
        return api;
    }
}
