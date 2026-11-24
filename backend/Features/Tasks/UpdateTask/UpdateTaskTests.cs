using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using TemplateApp.Tests.Support;
using TemplateApp.Tests.Support.Clients;
using TemplateApp.Tests.Support.Hosts;
using TemplateApp.Tests.Support.Probes;
using Xunit;

namespace TemplateApp.Features.Tasks.UpdateTask;

public sealed class UpdateTaskTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public UpdateTaskTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Update_PersistsFinalState_Idempotently()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        string id = await alice.CreateTaskAsync("Borrador");
        uint read = await alice.TaskVersionAsync(id);

        var updated = await alice.SendAsync(HttpMethod.Put, $"api/v1/tasks/{id}", new { title = "Estado final", isCompleted = true, version = read });
        Assert.Equal(HttpStatusCode.OK, updated.Status);
        Assert.NotNull(updated.Payload);
        using (updated.Payload)
        {
            Assert.Equal("Estado final", updated.Payload.RootElement.GetProperty("title").GetString());
            Assert.True(updated.Payload.RootElement.GetProperty("isCompleted").GetBoolean());
        }

        var repeated = await alice.SendAsync(HttpMethod.Put, $"api/v1/tasks/{id}", new { title = "Estado final", isCompleted = true, version = read });
        Assert.Equal(HttpStatusCode.OK, repeated.Status);
        Assert.NotNull(repeated.Payload);
        using (repeated.Payload)
            Assert.NotEqual(read, repeated.Payload.RootElement.GetProperty("version").GetUInt32());

        var fetched = await alice.SendAsync(HttpMethod.Get, $"api/v1/tasks/{id}", csrf: false);
        Assert.Equal(HttpStatusCode.OK, fetched.Status);
        Assert.NotNull(fetched.Payload);
        using (fetched.Payload)
            Assert.True(fetched.Payload.RootElement.GetProperty("isCompleted").GetBoolean());

        var invalid = await alice.SendAsync(HttpMethod.Put, $"api/v1/tasks/{id}", new { title = "", isCompleted = false, version = read });
        invalid.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.BadRequest, invalid.Status);

        var withoutVersion = await alice.SendAsync(HttpMethod.Put, $"api/v1/tasks/{id}", new { title = "Estado final", isCompleted = true });
        var problem = ProblemAssert.Problem(withoutVersion, HttpStatusCode.BadRequest, "Validation.Failed");
        Assert.True(problem.GetProperty("errors").TryGetProperty("Version", out _));

        var missing = await alice.SendAsync(HttpMethod.Put, $"api/v1/tasks/{Guid.NewGuid():D}", new { title = "x", isCompleted = false, version = read });
        missing.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.NotFound, missing.Status);
    }

    [Fact]
    public async Task Update_RecordsTimeToComplete_OnlyWhenTheTaskBecomesCompleted()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        var client = _fixture.CreateIsolatedTasksClient(services => services.AddSingleton<TimeProvider>(clock), out IServiceProvider services);
        var alice = new TestSession(client);
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        string id = await alice.CreateTaskAsync("Comprar pan");
        using var completion = new RecordedMeasurements(services, "TemplateApp.Tasks", "templateapp.tasks.completion.duration");

        clock.Advance(TimeSpan.FromMinutes(10));
        uint created = await alice.TaskVersionAsync(id);
        var renamed = await alice.SendAsync(HttpMethod.Put, $"api/v1/tasks/{id}", new { title = "Comprar pan integral", isCompleted = false, version = created });
        renamed.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, renamed.Status);
        Assert.Empty(completion.Values);

        clock.Advance(TimeSpan.FromMinutes(15));
        uint read = await alice.TaskVersionAsync(id);
        var completed = await alice.SendAsync(HttpMethod.Put, $"api/v1/tasks/{id}", new { title = "Comprar pan integral", isCompleted = true, version = read });
        var replay = await alice.SendAsync(HttpMethod.Put, $"api/v1/tasks/{id}", new { title = "Comprar pan integral", isCompleted = true, version = read });
        var stale = await alice.SendAsync(HttpMethod.Put, $"api/v1/tasks/{id}", new { title = "Otra cosa", isCompleted = true, version = read });
        completed.Payload?.Dispose();
        replay.Payload?.Dispose();

        Assert.Equal(HttpStatusCode.OK, completed.Status);
        Assert.Equal(HttpStatusCode.OK, replay.Status);
        ProblemAssert.Problem(stale, HttpStatusCode.Conflict, "Task.Conflict");
        var measurement = Assert.Single(completion.Values);
        Assert.Equal(TimeSpan.FromMinutes(25).TotalSeconds, measurement.Value);
        Assert.Empty(measurement.Tags);
    }

    [Fact]
    public async Task Update_WithStaleVersion_Returns409_And_KeepsOtherTabChange()
    {
        var tabA = new TestSession(_fixture.CreateClient());
        await tabA.RegisterAsync(TestAccounts.UniqueName("alice"));
        var tabB = tabA.CopyTo(_fixture.CreateClient());

        string id = await tabA.CreateTaskAsync("Comprar pan");
        uint read = await tabA.TaskVersionAsync(id);

        var renamed = await tabA.SendAsync(HttpMethod.Put, $"api/v1/tasks/{id}", new { title = "Comprar pan integral", isCompleted = false, version = read });
        renamed.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, renamed.Status);

        var stale = await tabB.SendAsync(HttpMethod.Put, $"api/v1/tasks/{id}", new { title = "Comprar pan", isCompleted = true, version = read });

        var fetched = await tabA.SendAsync(HttpMethod.Get, $"api/v1/tasks/{id}", csrf: false);
        Assert.Equal(HttpStatusCode.OK, fetched.Status);
        Assert.NotNull(fetched.Payload);
        using (fetched.Payload)
        {
            Assert.Equal("Comprar pan integral", fetched.Payload.RootElement.GetProperty("title").GetString());
            Assert.False(fetched.Payload.RootElement.GetProperty("isCompleted").GetBoolean());
        }

        ProblemAssert.Problem(stale, HttpStatusCode.Conflict, "Task.Conflict");
    }

    [Fact]
    public async Task Update_ConcurrentWithSameVersion_OneWins_OtherGets409()
    {
        var tabA = new TestSession(_fixture.CreateClient());
        await tabA.RegisterAsync(TestAccounts.UniqueName("alice"));
        var tabB = tabA.CopyTo(_fixture.CreateClient());

        string id = await tabA.CreateTaskAsync("Comprar pan");
        uint read = await tabA.TaskVersionAsync(id);

        var a = new { title = "Comprar pan integral", isCompleted = false, version = read };
        var b = new { title = "Comprar pan", isCompleted = true, version = read };
        SessionResponse[] responses = await Task.WhenAll(
            tabA.SendAsync(HttpMethod.Put, $"api/v1/tasks/{id}", a),
            tabB.SendAsync(HttpMethod.Put, $"api/v1/tasks/{id}", b));
        foreach (SessionResponse response in responses)
            response.Payload?.Dispose();

        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.Conflict],
            responses.Select(r => r.Status).Order().ToArray());

        var winner = responses[0].Status == HttpStatusCode.OK ? a : b;
        var fetched = await tabA.SendAsync(HttpMethod.Get, $"api/v1/tasks/{id}", csrf: false);
        Assert.Equal(HttpStatusCode.OK, fetched.Status);
        Assert.NotNull(fetched.Payload);
        using (fetched.Payload)
        {
            Assert.Equal(winner.title, fetched.Payload.RootElement.GetProperty("title").GetString());
            Assert.Equal(winner.isCompleted, fetched.Payload.RootElement.GetProperty("isCompleted").GetBoolean());
        }
    }
}
