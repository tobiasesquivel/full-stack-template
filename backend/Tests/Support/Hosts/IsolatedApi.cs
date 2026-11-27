using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using TemplateApp.Infrastructure.Messaging;
using TemplateApp.Infrastructure.Messaging.Inbox;
using TemplateApp.Infrastructure.Messaging.Outbox;
using TemplateApp.Infrastructure.Persistence;
using TemplateApp.Tests.Support.Clients;
using TemplateApp.Tests.Support.Rows;
using Xunit;

namespace TemplateApp.Tests.Support.Hosts;

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

    public IReadOnlyList<OutboxRelay> Relays =>
        [.. Hosts.SelectMany(host => host.GetKeyedServices<OutboxRelay>(KeyedService.AnyKey))];

    private IServiceProvider[] Hosts => [Pair.Monolith.Services, Pair.Tasks.Services];

    public OutboxRelay RelayOf<TContext>()
        where TContext : DbContext =>
        Hosts.Select(host => host.GetKeyedService<OutboxRelay>(typeof(TContext))).FirstOrDefault(relay => relay is not null)
            ?? throw new InvalidOperationException($"No host registers the outbox of {typeof(TContext).Name}.");

    public static async Task<IsolatedApi> StartAsync(
        string server,
        Uri broker,
        Action<IServiceCollection>? configure = null,
        Func<HttpRequestMessage, HttpMessageHandler, HttpMessageHandler?>? interceptRemote = null,
        IReadOnlyDictionary<string, string>? settings = null,
        bool kestrel = false) =>
        StartOn(
            new NpgsqlConnectionStringBuilder(await TestDatabases.CreateEmptyAsync(server)) { Pooling = false }.ConnectionString,
            broker,
            NewPrefix(),
            configure,
            interceptRemote,
            settings,
            kestrel);

    public static string NewPrefix() => $"t{Guid.NewGuid():N}"[..13];

    public static IsolatedApi StartOn(
        string database,
        Uri broker,
        string prefix,
        Action<IServiceCollection>? configure = null,
        Func<HttpRequestMessage, HttpMessageHandler, HttpMessageHandler?>? interceptRemote = null,
        IReadOnlyDictionary<string, string>? settings = null,
        bool kestrel = false)
    {
        FakeTimeProvider clock = new(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        Dictionary<string, string> all = new(settings ?? new Dictionary<string, string>())
        {
            [ApiPair.BrokerSetting] = broker.ToString(),
            ["Messaging:Prefix"] = prefix,
        };
        ApiPair pair = new(
            database,
            all,
            services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                configure?.Invoke(services);
            },
            interceptRemote,
            kestrel);
        pair.Start();
        return new IsolatedApi(database, prefix, pair, clock);
    }

    public async Task<TestSession> SignUpAsync(string name)
    {
        TestSession session = new(Pair.CreateClient());
        await session.RegisterAsync(TestAccounts.UniqueName(name));
        return session;
    }

    public async Task PublishOutboxAsync<TContext>()
        where TContext : DbContext
    {
        OutboxRelay relay = RelayOf<TContext>();
        while (await relay.RunOnceAsync(TestContext.Current.CancellationToken) > 0)
        {
        }
    }

    public async Task DrainAsync()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (true)
        {
            long inFlight = await ClosuresInFlightAsync();
            int published = 0;
            foreach (OutboxRelay relay in Relays)
                published += await relay.RunOnceAsync(ct);
            if (inFlight == 0 && published == 0 && await UndeliveredAsync() == 0)
                return;

            Assert.True(DateTime.UtcNow < deadline, "The outboxes did not settle in 60 s: a message is pending, some consumer never took it or a closure is still in flight.");
            if (published == 0)
                await Task.Delay(100, ct);
        }
    }

    public Task<long> ClosuresInFlightAsync() =>
        Pair.Monolith.Services.CountAsync(
            $"""
            SELECT (SELECT count(*) FROM users."AccountClosureSagas" WHERE "State" NOT IN ('Completed', 'Rejected'))
                 + (SELECT count(*) FROM wolverine.wolverine_outgoing_envelopes)
                 + (SELECT count(*) FROM wolverine.wolverine_incoming_envelopes WHERE status = 'Incoming') AS "Value"
            """);

    private Task<long> UndeliveredAsync() =>
        Pair.Monolith.Services.CountAsync(FormattableStringFactory.Create(
            $$"""
            WITH expected("Type", "Consumer") AS (VALUES
                ('users.user-deleted', 'tasks.user-deletions'),
                ('tasks.task-created', 'projects.task-counters'),
                ('tasks.task-completed', 'projects.task-counters'),
                ('tasks.task-reopened', 'projects.task-counters'),
                ('tasks.task-deleted', 'projects.task-counters')),
            outbox AS ({{UnionOf<OutboxMessage>("\"MessageId\", \"Type\", \"ProcessedAt\"")}}),
            inbox AS ({{UnionOf<InboxMessage>("\"MessageId\", \"Consumer\"")}})
            SELECT count(*) AS "Value" FROM outbox AS o
            WHERE o."ProcessedAt" IS NULL
               OR EXISTS (
                   SELECT 1 FROM expected AS e
                   WHERE e."Type" = o."Type"
                     AND NOT EXISTS (SELECT 1 FROM inbox AS i WHERE i."MessageId" = o."MessageId" AND i."Consumer" = e."Consumer"))
            """));

    private string UnionOf<TEntity>(string columns)
    {
        List<string> tables = [];
        foreach (IServiceProvider host in Hosts)
        {
            using IServiceScope scope = host.CreateScope();
            tables.AddRange(scope.ServiceProvider.MigratedContexts()
                .Where(context => context.Model.FindEntityType(typeof(TEntity)) is not null)
                .Select(MappedTable.Of<TEntity>));
        }

        return string.Join(" UNION ALL ", tables.Distinct().Select(table => $"SELECT {columns} FROM {table}"));
    }

    public void Dispose() => Pair.Dispose();
}
