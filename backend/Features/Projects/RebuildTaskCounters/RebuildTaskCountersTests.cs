using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Projects.RebuildTaskCounters;

public sealed class RebuildTaskCountersTests : IDisposable
{
    private readonly string _server;
    private readonly Uri _broker;
    private readonly List<IsolatedApi> _apis = [];

    public RebuildTaskCountersTests(PostgresFixture postgres, RabbitMqFixture broker)
    {
        _broker = broker.Uri;
        _server = postgres.ConnectionString;
    }

    [Fact]
    public async Task RebuildingFromScratch_GivesTheSameCounters()
    {
        IsolatedApi api = await StartAsync();
        await TaskCounterScenario.SeedAsync(api);
        await api.DrainOutboxAsync();
        List<TaskCounterRow> before = await api.Pair.Monolith.Services.AllTaskCountersAsync();

        await ExecuteSqlAsync(api, """DELETE FROM projects."ProjectTaskCounters" """);
        Assert.Empty(await api.Pair.Monolith.Services.AllTaskCountersAsync());
        await RebuildAsync(api);

        Assert.Equal(before, await api.Pair.Monolith.Services.AllTaskCountersAsync());
        Assert.Equal(await api.Pair.Monolith.Services.CountedTasksAsync(), before);
    }

    [Fact]
    public async Task EventsStillInTheOutbox_AreNotCountedTwice()
    {
        IsolatedApi api = await StartAsync();
        await TaskCounterScenario.SeedAsync(api);

        await RebuildAsync(api);
        await api.DrainOutboxAsync();

        Assert.Equal(await api.Pair.Monolith.Services.CountedTasksAsync(), await api.Pair.Monolith.Services.AllTaskCountersAsync());
    }

    [Fact]
    public async Task EventsAfterTheRebuild_AreCounted()
    {
        IsolatedApi api = await StartAsync();
        TestSession alice = await api.SignUpAsync("alice");
        string project = await alice.CreateProjectAsync("Casa");
        await alice.CreateTaskAsync("Antes", project);
        await RebuildAsync(api);

        await alice.CreateTaskAsync("Después", project);
        await api.DrainOutboxAsync();

        Assert.Equal((2, 0), await api.Pair.Monolith.Services.TaskCountersAsync(project));
    }

    [Fact]
    public async Task AMessageAppliedBeforeTheRebuild_ArrivingAgainAfterIt_IsNotCountedTwice()
    {
        IsolatedApi api = await StartAsync();
        TestSession alice = await api.SignUpAsync("alice");
        string project = await alice.CreateProjectAsync("Casa");
        await alice.CreateTaskAsync("Comprar pan", project);
        await api.DrainOutboxAsync();
        OutboxRow applied = Assert.Single(await api.Pair.Tasks.Services.AllOutboxRowsAsync());
        await RebuildAsync(api);

        using HttpClient monolith = api.Pair.CreateMonolithClient();
        using HttpResponseMessage late = await monolith.PutAsJsonAsync(
            $"internal/v1/task-events/{applied.MessageId}",
            new { type = applied.Type, version = applied.Version, occurredAt = applied.OccurredAt, payload = JsonDocument.Parse(applied.Payload).RootElement },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, late.StatusCode);
        Assert.Equal((1, 0), await api.Pair.Monolith.Services.TaskCountersAsync(project));
    }

    [Fact]
    public async Task OnStartup_AProjectWithoutCounters_GetsThemFromTasks()
    {
        IsolatedApi first = await StartAsync();
        TestSession alice = await first.SignUpAsync("alice");
        string project = await alice.CreateProjectAsync("Casa");
        await alice.CreateTaskAsync("Abierta", project);
        await first.DrainOutboxAsync();
        await ExecuteSqlAsync(first, """DELETE FROM projects."ProjectTaskCounters" """);

        IsolatedApi second = IsolatedApi.StartOn(first.Database, _broker, first.Prefix);
        _apis.Add(second);

        (int, int)? counters = null;
        for (int attempt = 0; attempt < 100 && counters is null; attempt++)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            counters = await second.Pair.Monolith.Services.TaskCountersAsync(project);
        }

        Assert.Equal((1, 0), counters);
    }

    public void Dispose()
    {
        foreach (IsolatedApi api in _apis)
            api.Dispose();
    }

    private static async Task RebuildAsync(IsolatedApi api)
    {
        await using AsyncServiceScope scope = api.Pair.Monolith.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<RebuildTaskCounters>().ExecuteAsync(TestContext.Current.CancellationToken);
    }

    private static async Task ExecuteSqlAsync(IsolatedApi api, string sql)
    {
        await using NpgsqlConnection connection = await TestDatabases.OpenAsync(api.Database);
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<IsolatedApi> StartAsync()
    {
        IsolatedApi api = await IsolatedApi.StartAsync(_server, _broker);
        _apis.Add(api);
        return api;
    }
}
