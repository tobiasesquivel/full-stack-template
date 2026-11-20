using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Features.Users.OrchestrateAccountClosure;
using TemplateApp.Features.Users.Persistence;
using TemplateApp.Infrastructure.Messaging;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Users.ScheduleAccountClosure;

public sealed class AccountClosureGracePeriodTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);

    private static readonly Dictionary<string, string> SecondsNotDays = new()
    {
        ["AccountClosure:GracePeriodSeconds"] = "6",
        ["AccountClosure:ReminderOffsetsSeconds:0"] = "2",
        ["AccountClosure:ReminderOffsetsSeconds:1"] = "4",
    };

    private readonly string _server;
    private readonly Uri _broker;
    private readonly List<IsolatedApi> _apis = [];

    public AccountClosureGracePeriodTests(PostgresFixture postgres, RabbitMqFixture rabbit)
    {
        _server = postgres.ConnectionString;
        _broker = rabbit.Uri;
    }

    [Fact]
    public async Task AGracePeriodOpenedByTheNextRelease_RemindsTwice_AndThenStartsTheClosure()
    {
        IsolatedApi api = await StartAsync(SecondsNotDays);
        using RecordedMeasurements reminders = new(api.Pair.Monolith.Services, "TemplateApp.Users", "templateapp.account_closures.reminders");
        TestSession bob = await api.SignUpAsync("bob");
        Stopwatch elapsed = Stopwatch.StartNew();

        Guid closure = await OpenAsNextReleaseAsync(api, bob.AuthId);
        List<(string Fact, TimeSpan At)> seen = await WatchAsync(api, closure, AccountClosureFacts.ClosureStarted, elapsed);
        await api.DrainAsync();

        Assert.Equal(
            ["requested", "reminder-sent:1", "reminder-sent:2", "grace-expired", "closure-started", "completed"],
            await HistoryAsync(api, closure));
        Assert.Equal(["reminder-sent:1", "reminder-sent:2", "grace-expired", "closure-started"], seen.Select(s => s.Fact));
        Assert.True(seen[0].At >= TimeSpan.FromSeconds(2), $"The first reminder came at {seen[0].At}, before its 2 s.");
        Assert.True(seen[1].At >= TimeSpan.FromSeconds(4), $"The second reminder came at {seen[1].At}, before its 4 s.");
        Assert.True(seen[2].At >= TimeSpan.FromSeconds(6), $"The grace period ended at {seen[2].At}, before its 6 s.");
        Assert.Equal(2, reminders.Values.Count);
        Assert.Equal(0, await GracePeriodsAsync(api, closure));
        Assert.Equal("Completed", await api.Pair.Monolith.Services.TextAsync($"""SELECT "State" AS "Value" FROM users."AccountClosureSagas" WHERE "ClosureId" = {closure}"""));
    }

    [Fact]
    public async Task TheClosureHistory_RejectsADirectUpdateOrDelete()
    {
        IsolatedApi api = await StartAsync();
        TestSession bob = await api.SignUpAsync("bob");
        Guid closure = await OpenAsNextReleaseAsync(api, bob.AuthId);
        await using NpgsqlConnection connection = await TestDatabases.OpenAsync(api.Database);

        PostgresException update = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync(connection, """UPDATE users."AccountClosureHistory" SET "Actor" = 'someone-else' WHERE "ClosureId" = $1""", closure));
        PostgresException delete = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync(connection, """DELETE FROM users."AccountClosureHistory" WHERE "ClosureId" = $1""", closure));

        Assert.Equal((PostgresErrorCodes.RestrictViolation, PostgresErrorCodes.RestrictViolation), (update.SqlState, delete.SqlState));
        Assert.Equal(["requested"], await HistoryAsync(api, closure));
    }

    public void Dispose()
    {
        foreach (IsolatedApi api in _apis)
            api.Dispose();
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

    private static async Task<Guid> OpenAsNextReleaseAsync(IsolatedApi api, string authId)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using IServiceScope scope = api.Pair.Monolith.Services.CreateScope();
        UsersDbContext db = scope.ServiceProvider.GetRequiredService<UsersDbContext>();
        AccountClosureOptions options = scope.ServiceProvider.GetRequiredService<IOptions<AccountClosureOptions>>().Value;
        DateTimeOffset now = api.Clock.GetUtcNow();
        Guid closureId = Guid.CreateVersion7(now);
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
        UserProfile profile = await db.UserProfiles.SingleAsync(p => p.AuthId == authId, ct);
        Assert.True(profile.ScheduleClosure(closureId, now, now + options.GracePeriod));
        await scope.ServiceProvider.GetRequiredService<AccountClosureHistory>()
            .RecordAsync(closureId, authId, AccountClosureFacts.Requested, authId, (now + options.GracePeriod).ToString("O"), now, ct);
        (AccountClosureGracePeriod saga, object[] messages) = AccountClosureGracePeriod.Open(closureId, authId, now, options.GracePeriod, options.Reminders);
        await scope.ServiceProvider.GetRequiredService<SagaOutbox<UsersDbContext>>().StartAndCommitAsync(saga, messages, ct);
        return closureId;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, Guid closureId)
    {
        await using NpgsqlCommand command = new(sql, connection);
        command.Parameters.Add(new NpgsqlParameter { Value = closureId });
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<IsolatedApi> StartAsync(Dictionary<string, string>? settings = null)
    {
        IsolatedApi api = await IsolatedApi.StartAsync(_server, _broker, FastScheduledMessages.Install, settings: settings);
        _apis.Add(api);
        return api;
    }
}
