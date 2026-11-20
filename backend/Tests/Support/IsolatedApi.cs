using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using TemplateApp.Features.Auth.Persistence;
using TemplateApp.Features.Tasks.Persistence;
using TemplateApp.Features.Users.Persistence;
using TemplateApp.Infrastructure.Messaging;
using Xunit;

namespace TemplateApp.Tests.Support;

internal sealed class IsolatedApi : IDisposable
{
    private IsolatedApi(string database, string prefix, ApiPair pair, FakeTimeProvider clock)
    {
        Database = database;
        Prefix = prefix;
        Pair = pair;
        Clock = clock;
    }

    public string Database { get; }

    public string Prefix { get; }

    public ApiPair Pair { get; }

    public FakeTimeProvider Clock { get; }

    public OutboxRelay Relay => Pair.Tasks.Services.GetRequiredKeyedService<OutboxRelay>(typeof(TasksDbContext));

    public OutboxRelay UsersRelay => Pair.Monolith.Services.GetRequiredKeyedService<OutboxRelay>(typeof(UsersDbContext));

    public OutboxRelay AuthRelay => Pair.Monolith.Services.GetRequiredKeyedService<OutboxRelay>(typeof(AuthDbContext));

    public static async Task<IsolatedApi> StartAsync(
        string server,
        Uri broker,
        Action<IServiceCollection>? configure = null,
        Func<HttpRequestMessage, HttpMessageHandler, HttpMessageHandler?>? interceptRemote = null) =>
        StartOn(
            new NpgsqlConnectionStringBuilder(await TestDatabases.CreateEmptyAsync(server)) { Pooling = false }.ConnectionString,
            broker,
            NewPrefix(),
            configure,
            interceptRemote);

    public static string NewPrefix() => $"t{Guid.NewGuid():N}"[..13];

    public static IsolatedApi StartOn(
        string database,
        Uri broker,
        string prefix,
        Action<IServiceCollection>? configure = null,
        Func<HttpRequestMessage, HttpMessageHandler, HttpMessageHandler?>? interceptRemote = null)
    {
        FakeTimeProvider clock = new(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        ApiPair pair = new(
            database,
            new Dictionary<string, string>
            {
                [ApiPair.BrokerSetting] = broker.ToString(),
                ["Messaging:Prefix"] = prefix,
            },
            services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                configure?.Invoke(services);
            },
            interceptRemote);
        pair.Start();
        return new IsolatedApi(database, prefix, pair, clock);
    }

    public async Task<TestSession> SignUpAsync(string name)
    {
        TestSession session = new(Pair.CreateClient());
        await session.RegisterAsync(TestAccounts.UniqueName(name));
        return session;
    }

    public async Task DrainOutboxAsync()
    {
        await PublishOutboxAsync();
        long unconsumed = await Eventually.Async(UnconsumedAsync, count => count == 0, TimeSpan.FromSeconds(30));
        Assert.Equal(0, unconsumed);
    }

    public async Task PublishOutboxAsync()
    {
        while (await Relay.RunOnceAsync(TestContext.Current.CancellationToken) > 0)
        {
        }
    }

    public async Task PublishUsersOutboxAsync()
    {
        while (await UsersRelay.RunOnceAsync(TestContext.Current.CancellationToken) > 0)
        {
        }
    }

    public async Task PublishAuthOutboxAsync()
    {
        while (await AuthRelay.RunOnceAsync(TestContext.Current.CancellationToken) > 0)
        {
        }
    }

    public async Task DrainAsync()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (true)
        {
            int published = await Relay.RunOnceAsync(ct) + await UsersRelay.RunOnceAsync(ct) + await AuthRelay.RunOnceAsync(ct);
            if (published == 0 && await UndeliveredAsync() == 0)
                return;

            Assert.True(DateTime.UtcNow < deadline, "The outboxes did not settle in 60 s: a message is pending or some consumer never took it.");
            if (published == 0)
                await Task.Delay(100, ct);
        }
    }

    private Task<long> UndeliveredAsync() =>
        Pair.Monolith.Services.CountAsync(
            $"""
            WITH expected("Type", "Consumer") AS (VALUES
                ('users.account-closure-requested', 'tasks.account-closures'),
                ('tasks.owner-tasks-held', 'auth.account-closures'),
                ('auth.account-deleted', 'users.account-closures'),
                ('auth.account-closure-rejected', 'users.account-closures'),
                ('auth.account-closure-rejected', 'tasks.account-closures'),
                ('users.user-deleted', 'tasks.user-deletions'),
                ('tasks.task-created', 'projects.task-counters'),
                ('tasks.task-completed', 'projects.task-counters'),
                ('tasks.task-reopened', 'projects.task-counters'),
                ('tasks.task-deleted', 'projects.task-counters')),
            outbox AS (
                SELECT "MessageId", "Type", "ProcessedAt" FROM users."OutboxMessages"
                UNION ALL SELECT "MessageId", "Type", "ProcessedAt" FROM tasks."OutboxMessages"
                UNION ALL SELECT "MessageId", "Type", "ProcessedAt" FROM auth."OutboxMessages"),
            inbox AS (
                SELECT "MessageId", "Consumer" FROM users."InboxMessages"
                UNION ALL SELECT "MessageId", "Consumer" FROM tasks."InboxMessages"
                UNION ALL SELECT "MessageId", "Consumer" FROM auth."InboxMessages"
                UNION ALL SELECT "MessageId", "Consumer" FROM projects."InboxMessages")
            SELECT count(*) AS "Value" FROM outbox AS o
            WHERE o."ProcessedAt" IS NULL
               OR EXISTS (
                   SELECT 1 FROM expected AS e
                   WHERE e."Type" = o."Type"
                     AND NOT EXISTS (SELECT 1 FROM inbox AS i WHERE i."MessageId" = o."MessageId" AND i."Consumer" = e."Consumer"))
            """);

    private Task<long> UnconsumedAsync() =>
        Pair.Monolith.Services.CountAsync(
            $"""
            SELECT count(*) AS "Value" FROM tasks."OutboxMessages" AS o
            WHERE o."ProcessedAt" IS NOT NULL
              AND o."Type" LIKE 'tasks.task-%'
              AND NOT EXISTS (SELECT 1 FROM projects."InboxMessages" AS i WHERE i."MessageId" = o."MessageId")
            """);

    public void Dispose() => Pair.Dispose();
}
