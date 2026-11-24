using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TemplateApp.Features.Users.DeleteUser;
using TemplateApp.Tests.Support;
using Wolverine;
using Xunit;

namespace TemplateApp.Features.Users.ScheduleAccountClosure;

public sealed class AccountClosureGracePeriodTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);

    private readonly string _server;
    private readonly Uri _broker;
    private readonly List<IsolatedApi> _apis = [];

    public AccountClosureGracePeriodTests(PostgresFixture postgres, RabbitMqFixture rabbit)
    {
        _server = postgres.ConnectionString;
        _broker = rabbit.Uri;
    }

    [Fact]
    public async Task AGracePeriodOfDays_RunWithSecondsForDays_RemindsTwice_AndThenStartsTheClosure()
    {
        IsolatedApi api = await StartAsync(GracePeriod(6, 2, 4));
        using RecordedMeasurements reminders = new(api.Pair.Monolith.Services, "TemplateApp.Users", "templateapp.account_closures.reminders");
        TestSession bob = await api.SignUpAsync("bob");
        Stopwatch elapsed = Stopwatch.StartNew();

        (_, Guid closure, _) = await DeleteUserTests.ScheduleClosureAsync(bob, "api/v1/users/me");
        List<(string Fact, TimeSpan At)> seen = await WatchAsync(api, closure, AccountClosureFacts.ClosureStarted, elapsed);
        JsonElement closing = await DeleteUserTests.MeAsync(bob, "api/v1/users/me");
        await api.DrainAsync();

        Assert.Equal(["reminder-sent:1", "reminder-sent:2", "grace-expired", "closure-started"], seen.Select(s => s.Fact));
        Assert.True(seen[0].At >= TimeSpan.FromSeconds(2), $"The first reminder came at {seen[0].At}, before its 2 s.");
        Assert.True(seen[1].At >= TimeSpan.FromSeconds(4), $"The second reminder came at {seen[1].At}, before its 4 s.");
        Assert.True(seen[2].At >= TimeSpan.FromSeconds(6), $"The grace period ended at {seen[2].At}, before its 6 s.");
        Assert.Equal("closing", closing.GetProperty("accountStatus").GetString());
        Assert.Equal(
            ["requested", "reminder-sent:1", "reminder-sent:2", "grace-expired", "closure-started", "completed"],
            await HistoryAsync(api, closure));
        Assert.Equal(2, reminders.Values.Count);
        Assert.Equal(0, await GracePeriodsAsync(api, closure));
        Assert.Equal("Completed", await DeleteUserBrokerTests.SagaStateAsync(api, closure));
        Assert.Equal(OwnedRows.None, await api.Pair.Monolith.Services.OwnedRowsAsync(bob.AuthId));
    }

    [Fact]
    public async Task ACancellationThatQueuesBehindTheExpiry_Gets409_AndTheClosureRunsToTheEnd()
    {
        IsolatedApi api = await StartAsync(GracePeriod(3, 1, 2));
        TestSession bob = await api.SignUpAsync("bob");
        (_, Guid closure, _) = await DeleteUserTests.ScheduleClosureAsync(bob, "api/v1/users/me");
        CancellationToken ct = TestContext.Current.CancellationToken;

        await using NpgsqlConnection held = await TestDatabases.OpenAsync(api.Database);
        await using NpgsqlTransaction holding = await LockProfileAsync(held, bob.AuthId);
        long expiryWaits = await Eventually.Async(() => WaitingForLocksAsync(api), count => count == 1, Wait);
        Task<SessionResponse> cancel = CancelAsync(bob, closure);
        long bothWait = await Eventually.Async(() => WaitingForLocksAsync(api), count => count == 2, Wait);
        await holding.CommitAsync(ct);
        SessionResponse cancelled = await cancel;
        await api.DrainAsync();

        Assert.Equal((1, 2), (expiryWaits, bothWait));
        ProblemAssert.Problem(cancelled, HttpStatusCode.Conflict, "User.ClosureNotCancellable");
        Assert.Equal(["requested", "reminder-sent:1", "reminder-sent:2", "grace-expired", "closure-started", "completed"], await HistoryAsync(api, closure));
        Assert.Equal("Completed", await DeleteUserBrokerTests.SagaStateAsync(api, closure));
        Assert.Equal(OwnedRows.None, await api.Pair.Monolith.Services.OwnedRowsAsync(bob.AuthId));
    }

    [Fact]
    public async Task AnExpiryThatQueuesBehindTheCancellation_StartsNothing_AndTheGracePeriodEndsCancelled()
    {
        IsolatedApi api = await StartAsync(GracePeriod(3, 1, 2));
        TestSession bob = await api.SignUpAsync("bob");
        (_, Guid closure, _) = await DeleteUserTests.ScheduleClosureAsync(bob, "api/v1/users/me");
        CancellationToken ct = TestContext.Current.CancellationToken;

        await using NpgsqlConnection held = await TestDatabases.OpenAsync(api.Database);
        await using NpgsqlTransaction holding = await LockProfileAsync(held, bob.AuthId);
        Task<SessionResponse> cancel = CancelAsync(bob, closure);
        long cancelWaits = await Eventually.Async(() => WaitingForLocksAsync(api), count => count == 1, Wait);
        long bothWait = await Eventually.Async(() => WaitingForLocksAsync(api), count => count == 2, Wait);
        await holding.CommitAsync(ct);
        SessionResponse cancelled = await cancel;
        cancelled.Payload?.Dispose();
        long timersLeft = await Eventually.Async(() => ScheduledTimersAsync(api), count => count == 0, Wait);
        long gracePeriods = await Eventually.Async(() => GracePeriodsAsync(api, closure), count => count == 0, Wait);
        JsonElement me = await DeleteUserTests.MeAsync(bob, "api/v1/users/me");

        Assert.Equal((1, 2), (cancelWaits, bothWait));
        Assert.Equal(HttpStatusCode.NoContent, cancelled.Status);
        Assert.Equal((0, 0), (timersLeft, gracePeriods));
        Assert.Equal("active", me.GetProperty("accountStatus").GetString());
        Assert.Equal("cancelled", me.GetProperty("lastClosure").GetProperty("outcome").GetString());
        Assert.Null(await DeleteUserBrokerTests.SagaStateAsync(api, closure));
        string[] history = await HistoryAsync(api, closure);
        Assert.DoesNotContain("closure-started", history);
        Assert.Equal("cancelled", history[^1]);
    }

    [Fact]
    public async Task AReminderDeliveredTwice_LeavesOneHistoryRow_AndCountsOnce()
    {
        IsolatedApi api = await StartAsync([]);
        using RecordedMeasurements reminders = new(api.Pair.Monolith.Services, "TemplateApp.Users", "templateapp.account_closures.reminders");
        TestSession bob = await api.SignUpAsync("bob");
        (_, Guid closure, _) = await DeleteUserTests.ScheduleClosureAsync(bob, "api/v1/users/me");

        await RemindAsync(api, closure, 1);
        await RemindAsync(api, closure, 1);

        Assert.Equal(["requested", "reminder-sent:1"], await HistoryAsync(api, closure));
        Assert.Single(reminders.Values);
        Assert.Equal("1", await api.Pair.Monolith.Services.TextAsync(
            $"""SELECT "RemindersSent"::text AS "Value" FROM users."AccountClosureGracePeriods" WHERE "ClosureId" = {closure}"""));
    }

    public void Dispose()
    {
        foreach (IsolatedApi api in _apis)
            api.Dispose();
    }

    internal static Dictionary<string, string> GracePeriod(int seconds, int firstReminder, int secondReminder) => new()
    {
        ["AccountClosure:GracePeriodSeconds"] = seconds.ToString(CultureInfo.InvariantCulture),
        ["AccountClosure:ReminderOffsetsSeconds:0"] = firstReminder.ToString(CultureInfo.InvariantCulture),
        ["AccountClosure:ReminderOffsetsSeconds:1"] = secondReminder.ToString(CultureInfo.InvariantCulture),
    };

    internal static async Task EndGracePeriodAsync(IsolatedApi api, Guid closureId)
    {
        using IServiceScope scope = api.Pair.Monolith.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeAsync(new AccountClosureGraceExpired(closureId, TimeSpan.Zero), TestContext.Current.CancellationToken);
    }

    private static async Task RemindAsync(IsolatedApi api, Guid closureId, int number)
    {
        using IServiceScope scope = api.Pair.Monolith.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeAsync(new AccountClosureReminderDue(closureId, number, TimeSpan.Zero), TestContext.Current.CancellationToken);
    }

    internal static async Task<string[]> HistoryAsync(IsolatedApi api, Guid closureId)
    {
        string? rows = await api.Pair.Monolith.Services.TextAsync(
            $"""
            SELECT string_agg(CASE WHEN "Kind" = 'reminder-sent' THEN "Kind" || ':' || "Detail" ELSE "Kind" END, ',' ORDER BY "Id") AS "Value"
            FROM users."AccountClosureHistory" WHERE "ClosureId" = {closureId}
            """);
        return rows is null ? [] : rows.Split(',');
    }

    internal static Task<long> GracePeriodsAsync(IsolatedApi api, Guid closureId) =>
        api.Pair.Monolith.Services.CountAsync($"""SELECT count(*) AS "Value" FROM users."AccountClosureGracePeriods" WHERE "ClosureId" = {closureId}""");

    internal static Task<long> ScheduledTimersAsync(IsolatedApi api) =>
        api.Pair.Monolith.Services.CountAsync(
            $"""
            SELECT count(*) AS "Value" FROM wolverine.wolverine_incoming_envelopes
            WHERE status IN ('Scheduled', 'Incoming') AND (message_type LIKE '%AccountClosureReminderDue%' OR message_type LIKE '%AccountClosureGraceExpired%')
            """);

    internal static Task<SessionResponse> CancelAsync(TestSession session, Guid closureId) =>
        session.SendAsync(HttpMethod.Post, $"api/v1/users/closures/{closureId}/cancel");

    internal static async Task<List<(string Fact, TimeSpan At)>> WatchAsync(IsolatedApi api, Guid closureId, string until, Stopwatch elapsed)
    {
        List<(string Fact, TimeSpan At)> seen = [];
        DateTime deadline = DateTime.UtcNow + Wait;
        while (DateTime.UtcNow < deadline)
        {
            string[] history = await HistoryAsync(api, closureId);
            foreach (string fact in history.Skip(1 + seen.Count))
                seen.Add((fact, elapsed.Elapsed));
            if (seen.Any(s => s.Fact == until))
                return seen;

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        return seen;
    }

    private static async Task<NpgsqlTransaction> LockProfileAsync(NpgsqlConnection connection, string authId)
    {
        NpgsqlTransaction transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using NpgsqlCommand take = new("""SELECT 1 FROM users."UserProfiles" WHERE "AuthId" = $1 FOR UPDATE""", connection, transaction);
        take.Parameters.Add(new NpgsqlParameter { Value = authId });
        await take.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return transaction;
    }

    private static Task<long> WaitingForLocksAsync(IsolatedApi api) =>
        api.Pair.Monolith.Services.CountAsync(
            $"""
            SELECT count(*) AS "Value" FROM pg_stat_activity
            WHERE datname = current_database() AND wait_event_type = 'Lock' AND query LIKE '%UserProfiles%FOR UPDATE%'
            """);

    private async Task<IsolatedApi> StartAsync(Dictionary<string, string> settings)
    {
        IsolatedApi api = await IsolatedApi.StartAsync(_server, _broker, FastScheduledMessages.Install, settings: settings);
        _apis.Add(api);
        return api;
    }
}
