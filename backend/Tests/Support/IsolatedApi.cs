using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using TemplateApp.Features.Tasks.Persistence;
using TemplateApp.Infrastructure.Messaging;
using Xunit;

namespace TemplateApp.Tests.Support;

internal sealed class IsolatedApi : IDisposable
{
    private IsolatedApi(string database, ApiPair pair, FakeTimeProvider clock)
    {
        Database = database;
        Pair = pair;
        Clock = clock;
    }

    public string Database { get; }

    public ApiPair Pair { get; }

    public FakeTimeProvider Clock { get; }

    public OutboxRelay Relay => Pair.Tasks.Services.GetRequiredKeyedService<OutboxRelay>(typeof(TasksDbContext));

    public static async Task<IsolatedApi> StartAsync(
        string server,
        Action<IServiceCollection>? configure = null,
        Func<HttpRequestMessage, HttpMessageHandler, HttpMessageHandler?>? interceptRemote = null) =>
        StartOn(await TestDatabases.CreateEmptyAsync(server), configure, interceptRemote);

    public static IsolatedApi StartOn(
        string database,
        Action<IServiceCollection>? configure = null,
        Func<HttpRequestMessage, HttpMessageHandler, HttpMessageHandler?>? interceptRemote = null)
    {
        FakeTimeProvider clock = new(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        ApiPair pair = new(
            database,
            new Dictionary<string, string>(),
            services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                configure?.Invoke(services);
            },
            interceptRemote);
        pair.Start();
        return new IsolatedApi(database, pair, clock);
    }

    public async Task<TestSession> SignUpAsync(string name)
    {
        TestSession session = new(Pair.CreateClient());
        await session.RegisterAsync(TestAccounts.UniqueName(name));
        return session;
    }

    public async Task DrainOutboxAsync()
    {
        while (await Relay.RunOnceAsync(TestContext.Current.CancellationToken) > 0)
        {
        }
    }

    public void Dispose() => Pair.Dispose();
}
