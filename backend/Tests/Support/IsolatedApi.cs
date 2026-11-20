using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using TemplateApp.Features.Tasks.Persistence;
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

    private Task<long> UnconsumedAsync() =>
        Pair.Monolith.Services.CountAsync(
            $"""
            SELECT count(*) AS "Value" FROM tasks."OutboxMessages" AS o
            WHERE o."ProcessedAt" IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM projects."InboxMessages" AS i WHERE i."MessageId" = o."MessageId")
            """);

    public void Dispose() => Pair.Dispose();
}
