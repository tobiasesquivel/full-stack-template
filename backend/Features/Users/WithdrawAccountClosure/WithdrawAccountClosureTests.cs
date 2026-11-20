using System.Net;
using System.Text.Json;
using TemplateApp.Features.Users.DeleteUser;
using TemplateApp.Features.Users.ScheduleAccountClosure;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Users.WithdrawAccountClosure;

public sealed class WithdrawAccountClosureTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);

    private readonly string _server;
    private readonly Uri _broker;
    private readonly List<IsolatedApi> _apis = [];

    public WithdrawAccountClosureTests(PostgresFixture postgres, RabbitMqFixture rabbit)
    {
        _server = postgres.ConnectionString;
        _broker = rabbit.Uri;
    }

    [Fact]
    public async Task CancellingMidGracePeriod_StopsTheRemindersAndTheClosure_AndBringsTheAccountBack()
    {
        IsolatedApi api = await StartAsync(AccountClosureGracePeriodTests.GracePeriod(6, 2, 4));
        using RecordedMeasurements reminders = new(api.Pair.Monolith.Services, "TemplateApp.Users", "templateapp.account_closures.reminders");
        TestSession bob = await api.SignUpAsync("bob");
        (_, Guid closure, _) = await DeleteUserTests.ScheduleClosureAsync(bob, "api/v1/users/me");
        string[] firstReminder = await Eventually.Async(() => AccountClosureGracePeriodTests.HistoryAsync(api, closure), facts => facts.Length == 2, Wait);

        SessionResponse cancelled = await AccountClosureGracePeriodTests.CancelAsync(bob, closure);
        cancelled.Payload?.Dispose();
        long gracePeriods = await AccountClosureGracePeriodTests.GracePeriodsAsync(api, closure);
        long timersLeft = await Eventually.Async(() => AccountClosureGracePeriodTests.ScheduledTimersAsync(api), count => count == 0, Wait);
        JsonElement me = await DeleteUserTests.MeAsync(bob, "api/v1/users/me");

        Assert.Equal(["requested", "reminder-sent:1"], firstReminder);
        Assert.Equal(HttpStatusCode.NoContent, cancelled.Status);
        Assert.Equal((0, 0), (gracePeriods, timersLeft));
        Assert.Equal(["requested", "reminder-sent:1", "cancelled"], await AccountClosureGracePeriodTests.HistoryAsync(api, closure));
        Assert.Single(reminders.Values);
        Assert.Equal("active", me.GetProperty("accountStatus").GetString());
        Assert.Equal(JsonValueKind.Null, me.GetProperty("closureId").ValueKind);
        Assert.Equal((closure, "cancelled"), (me.GetProperty("lastClosure").GetProperty("closureId").GetGuid(), me.GetProperty("lastClosure").GetProperty("outcome").GetString()));
        Assert.Null(await DeleteUserBrokerTests.SagaStateAsync(api, closure));
        Assert.Equal(bob.AuthId, await api.Pair.Monolith.Services.TextAsync(
            $"""SELECT "Actor" AS "Value" FROM users."AccountClosureHistory" WHERE "ClosureId" = {closure} AND "Kind" = 'cancelled'"""));
        await bob.CreateTaskAsync("Después de cancelar");
        (_, Guid next, _) = await DeleteUserTests.ScheduleClosureAsync(bob, "api/v1/users/me");
        Assert.NotEqual(closure, next);
    }

    [Fact]
    public async Task OnlyTheOwnerOrAUserManagerCancels_TheSameKeyRepeatsThe204_AndAStartedClosureCannotBeCancelled()
    {
        IsolatedApi api = await StartAsync();
        TestSession alice = await api.SignUpAsync("alice");
        TestSession eve = await api.SignUpAsync("eve");
        TestSession admin = await api.SignUpAsync("admin");
        await api.Pair.Monolith.Services.SetRoleAsync(admin.UserName, "SuperAdmin", member: true);
        (_, Guid scheduled, _) = await DeleteUserTests.ScheduleClosureAsync(alice, "api/v1/users/me");
        Dictionary<string, string> key = new() { ["Idempotency-Key"] = Guid.NewGuid().ToString() };

        SessionResponse unknown = await AccountClosureGracePeriodTests.CancelAsync(alice, Guid.NewGuid());
        SessionResponse byEve = await AccountClosureGracePeriodTests.CancelAsync(eve, scheduled);
        SessionResponse byAdmin = await admin.SendAsync(HttpMethod.Post, $"api/v1/users/closures/{scheduled}/cancel", headers: key);
        byAdmin.Payload?.Dispose();
        SessionResponse replayed = await admin.SendAsync(HttpMethod.Post, $"api/v1/users/closures/{scheduled}/cancel", headers: key);
        replayed.Payload?.Dispose();
        SessionResponse again = await AccountClosureGracePeriodTests.CancelAsync(alice, scheduled);
        (_, Guid started) = await DeleteUserTests.RequestClosureAsync(api, alice, "api/v1/users/me");
        SessionResponse tooLate = await AccountClosureGracePeriodTests.CancelAsync(alice, started);
        await api.DrainAsync();

        ProblemAssert.Problem(unknown, HttpStatusCode.NotFound, "User.ClosureNotFound");
        ProblemAssert.Problem(byEve, HttpStatusCode.Forbidden, "User.Forbidden");
        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.NoContent), (byAdmin.Status, replayed.Status));
        Assert.Equal("true", Assert.Contains("Idempotent-Replayed", replayed.Headers));
        ProblemAssert.Problem(again, HttpStatusCode.Conflict, "User.ClosureNotCancellable");
        ProblemAssert.Problem(tooLate, HttpStatusCode.Conflict, "User.ClosureNotCancellable");
        Assert.Equal(admin.AuthId, await api.Pair.Monolith.Services.TextAsync(
            $"""SELECT "Actor" AS "Value" FROM users."AccountClosureHistory" WHERE "ClosureId" = {scheduled} AND "Kind" = 'cancelled'"""));
        Assert.Equal(["requested", "cancelled"], await AccountClosureGracePeriodTests.HistoryAsync(api, scheduled));
        Assert.Equal("Completed", await DeleteUserBrokerTests.SagaStateAsync(api, started));
    }

    public void Dispose()
    {
        foreach (IsolatedApi api in _apis)
            api.Dispose();
    }

    private async Task<IsolatedApi> StartAsync(Dictionary<string, string>? settings = null)
    {
        IsolatedApi api = await IsolatedApi.StartAsync(_server, _broker, FastScheduledMessages.Install, settings: settings);
        _apis.Add(api);
        return api;
    }
}
