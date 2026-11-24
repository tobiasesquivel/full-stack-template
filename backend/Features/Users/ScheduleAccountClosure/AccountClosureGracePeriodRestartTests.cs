using System.Diagnostics;
using TemplateApp.Features.Users.DeleteUser;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Tests.Support.Bus;
using TemplateApp.Tests.Support.Clients;
using TemplateApp.Tests.Support.Hosts;
using Xunit;

namespace TemplateApp.Features.Users.ScheduleAccountClosure;

public sealed class AccountClosureGracePeriodRestartTests : IDisposable
{
    private readonly string _server;
    private readonly Uri _broker;
    private readonly List<IsolatedApi> _apis = [];

    public AccountClosureGracePeriodRestartTests(PostgresFixture postgres, RabbitMqFixture rabbit)
    {
        _server = postgres.ConnectionString;
        _broker = rabbit.Uri;
    }

    [Fact]
    public async Task TheMonolithRestartsWhileAReminderIsPending_AndTheGracePeriodResumes_EachStepOnce()
    {
        Dictionary<string, string> settings = AccountClosureGracePeriodTests.GracePeriod(40, 20, 30);
        IsolatedApi before = await StartAsync(settings);
        TestSession bob = await before.SignUpAsync("bob");

        Stopwatch elapsed = Stopwatch.StartNew();
        (_, Guid closure, _) = await DeleteUserTests.ScheduleClosureAsync(bob, "api/v1/users/me");
        long pending = await AccountClosureGracePeriodTests.ScheduledTimersAsync(before);
        before.Dispose();
        _apis.Remove(before);
        IsolatedApi after = IsolatedApi.StartOn(before.Database, _broker, before.Prefix, FastScheduledMessages.Install, settings: settings);
        _apis.Add(after);
        TimeSpan restarted = elapsed.Elapsed;
        List<(string Fact, TimeSpan At)> seen = await AccountClosureGracePeriodTests.WatchAsync(after, closure, AccountClosureFacts.ClosureStarted, elapsed);
        await after.DrainAsync();

        Assert.Equal(3, pending);
        Assert.True(restarted < TimeSpan.FromSeconds(20), $"The new hosts took {restarted} to start: the first reminder was already due.");
        Assert.True(seen[0].At >= TimeSpan.FromSeconds(20), $"The first reminder came at {seen[0].At}, before its 20 s.");
        Assert.Equal(["reminder-sent:1", "reminder-sent:2", "grace-expired", "closure-started"], seen.Select(s => s.Fact));
        Assert.Equal(
            ["requested", "reminder-sent:1", "reminder-sent:2", "grace-expired", "closure-started", "completed"],
            await AccountClosureGracePeriodTests.HistoryAsync(after, closure));
        Assert.Equal(0, await AccountClosureGracePeriodTests.GracePeriodsAsync(after, closure));
        Assert.Equal("Completed", await DeleteUserBrokerTests.SagaStateAsync(after, closure));
    }

    public void Dispose()
    {
        foreach (IsolatedApi api in _apis)
            api.Dispose();
    }

    private async Task<IsolatedApi> StartAsync(Dictionary<string, string> settings)
    {
        IsolatedApi api = await IsolatedApi.StartAsync(_server, _broker, FastScheduledMessages.Install, settings: settings);
        _apis.Add(api);
        return api;
    }
}
