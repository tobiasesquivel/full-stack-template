using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TemplateApp.Tests.Support;
using TemplateApp.Tests.Support.Bus;
using TemplateApp.Tests.Support.Clients;
using TemplateApp.Tests.Support.Hosts;
using TemplateApp.Tests.Support.Probes;
using TemplateApp.Tests.Support.Rows;
using Xunit;

namespace TemplateApp.Features.Users.DeleteUser;

public sealed class DeleteUserBrokerTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);

    private readonly string _server;
    private readonly Uri _broker;
    private readonly List<IsolatedApi> _apis = [];

    public DeleteUserBrokerTests(PostgresFixture postgres, RabbitMqFixture rabbit)
    {
        _server = postgres.ConnectionString;
        _broker = rabbit.Uri;
    }

    [Fact]
    public async Task TheLastSuperAdminAsksToClose_AuthRejects_AndBothEarlierStepsCompensate()
    {
        CapturedLogs logs = new();
        IsolatedApi api = await StartAsync(services => services.AddSingleton<ILoggerFactory>(new LoggerFactory([logs])));
        using RecordedMeasurements finished = new(api.Pair.Monolith.Services, "TemplateApp.Users", "templateapp.account_closures.finished");
        TestSession alice = await api.SignUpAsync("alice");
        await api.Pair.Monolith.Services.SetRoleAsync(alice.UserName, "SuperAdmin", member: true);
        await alice.CreateTaskAsync("Primera");
        await alice.CreateTaskAsync("Segunda");
        await alice.CreateProjectAsync("Mudanza");
        await api.DrainAsync();

        (_, Guid closure) = await DeleteUserTests.RequestClosureAsync(api, alice, "api/v1/users/me");
        Assert.Equal(1, await Eventually.Async(() => HoldsAsync(api, alice.AuthId), holds => holds == 1, Wait));
        SessionResponse whileClosing = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "Durante el cierre" });
        JsonElement closing = await DeleteUserTests.MeAsync(alice, "api/v1/users/me");
        await api.DrainAsync();

        ProblemAssert.Problem(whileClosing, HttpStatusCode.Conflict, "Task.OwnerClosing");
        Assert.Equal("closing", closing.GetProperty("accountStatus").GetString());
        JsonElement me = await DeleteUserTests.MeAsync(alice, "api/v1/users/me");
        Assert.Equal("active", me.GetProperty("accountStatus").GetString());
        JsonElement last = me.GetProperty("lastClosure");
        Assert.Equal((closure, "rejected", "Auth.LastSuperAdmin"), (last.GetProperty("closureId").GetGuid(), last.GetProperty("outcome").GetString(), last.GetProperty("reason").GetString()));
        Assert.Equal(new OwnedRows(1, 2, 1, 1, 2, 1, 0), await api.Pair.Monolith.Services.OwnedRowsAsync(alice.AuthId));
        Assert.Equal(0, await HoldsAsync(api, alice.AuthId));
        Assert.Equal("Rejected", await DecisionAsync(api, closure));
        await alice.CreateTaskAsync("Después del rechazo");
        Assert.Equal(["rejected"], finished.Values.Select(v => (string)v.Tags["templateapp.account_closure.outcome"]!));
        string[] steps = logs.Entries
            .Where(entry => entry.Message.Contains(closure.ToString(), StringComparison.Ordinal))
            .Select(entry => entry.Category[(entry.Category.LastIndexOf('.') + 1)..])
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["AccountClosureGracePeriod", "AccountClosureSaga", "CancelAccountClosure", "DecideAccountClosure", "DeleteUserCommand", "HoldOwnerTasks", "ReleaseOwnerTasks", "StartAccountClosure"], steps.Order(StringComparer.Ordinal));
        Assert.Equal("Rejected", await SagaStateAsync(api, closure));
    }

    [Fact]
    public async Task AnOrdinaryUserAsksToClose_TheClosureCompletes_AndNoRowOfTheAccountStays()
    {
        IsolatedApi api = await StartAsync();
        using RecordedMeasurements finished = new(api.Pair.Monolith.Services, "TemplateApp.Users", "templateapp.account_closures.finished");
        TestSession bob = await api.SignUpAsync("bob");
        await bob.CreateTaskAsync("Sin clave");
        await bob.CreateProjectAsync("Mudanza");
        SessionResponse keyed = await bob.SendAsync(
            HttpMethod.Post,
            "api/v1/tasks",
            new { title = "Con clave" },
            headers: new Dictionary<string, string> { ["Idempotency-Key"] = Guid.NewGuid().ToString() });
        keyed.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Created, keyed.Status);
        Assert.Equal(new OwnedRows(1, 1, 1, 1, 2, 1, 1), await api.Pair.Monolith.Services.OwnedRowsAsync(bob.AuthId));

        (_, Guid closure) = await DeleteUserTests.RequestClosureAsync(api, bob, "api/v1/users/me");
        await api.DrainAsync();

        SessionResponse denied = await bob.SendAsync(HttpMethod.Get, "api/v1/users/me", csrf: false);
        denied.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, denied.Status);
        Assert.Equal(OwnedRows.None, await api.Pair.Monolith.Services.OwnedRowsAsync(bob.AuthId));
        Assert.Equal(0, await HoldsAsync(api, bob.AuthId));
        Assert.Equal(1, await api.Pair.Monolith.Services.CountAsync($"""SELECT count(*) AS "Value" FROM tasks."DeletedOwners" WHERE "OwnerId" = {bob.AuthId}"""));
        Assert.Equal("Completed", await DecisionAsync(api, closure));
        Assert.Equal("Completed", await SagaStateAsync(api, closure));
        Assert.Equal(1, await api.Pair.Monolith.Services.CountAsync(
            $"""SELECT count(*) AS "Value" FROM users."OutboxMessages" WHERE "Type" = 'users.user-deleted' AND "Payload"->>'authId' = {bob.AuthId}"""));
        Assert.Equal(["completed"], finished.Values.Select(v => (string)v.Tags["templateapp.account_closure.outcome"]!));
        AuditRow profileDeletion = (await api.Pair.Monolith.Services.AuditEntriesAsync(bob.AuthId)).Last();
        Assert.Equal(("Deleted", "system", null), (profileDeletion.Operation, profileDeletion.ActorKind, profileDeletion.ActorId));
        await bob.CsrfAsync();
        SessionResponse relogin = await bob.SendAsync(HttpMethod.Post, "api/v1/auth/login", new { userName = bob.UserName, password = bob.Password });
        relogin.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, relogin.Status);
    }

    [Fact]
    public async Task WithTasksDown_TheClosureWaitsClosing_AndCompletesWhenTasksComesBack()
    {
        IsolatedApi api = await StartAsync();
        TestSession alice = await api.SignUpAsync("alice");
        await alice.CreateTaskAsync("Hecha");
        await alice.CreateTaskAsync("Abierta");
        await TasksListener.StopAsync(api);

        await DeleteUserTests.RequestClosureAsync(api, alice, "api/v1/users/me");
        await api.PublishUsersOutboxAsync();
        await api.PublishAuthOutboxAsync();
        JsonElement whileDown = await DeleteUserTests.MeAsync(alice, "api/v1/users/me");
        OwnedRows rowsWhileDown = await api.Pair.Monolith.Services.OwnedRowsAsync(alice.AuthId);
        await TasksListener.StartAsync(api);
        await api.DrainAsync();

        Assert.Equal("closing", whileDown.GetProperty("accountStatus").GetString());
        Assert.Equal(new OwnedRows(1, 1, 1, 1, 2, 0, 0), rowsWhileDown);
        Assert.Equal(OwnedRows.None, await api.Pair.Monolith.Services.OwnedRowsAsync(alice.AuthId));
    }

    public void Dispose()
    {
        foreach (IsolatedApi api in _apis)
            api.Dispose();
    }

    internal static Task<long> HoldsAsync(IsolatedApi api, string ownerId) =>
        api.Pair.Monolith.Services.CountAsync($"""SELECT count(*) AS "Value" FROM tasks."OwnerHolds" WHERE "OwnerId" = {ownerId}""");

    internal static Task<string?> SagaStateAsync(IsolatedApi api, Guid closureId) =>
        api.Pair.Monolith.Services.TextAsync($"""SELECT "State" AS "Value" FROM users."AccountClosureSagas" WHERE "ClosureId" = {closureId}""");

    internal static Task<string?> DecisionAsync(IsolatedApi api, Guid closureId) =>
        api.Pair.Monolith.Services.TextAsync($"""SELECT "Outcome" AS "Value" FROM auth."AccountClosures" WHERE "ClosureId" = {closureId}""");

    private async Task<IsolatedApi> StartAsync(Action<IServiceCollection>? configure = null)
    {
        IsolatedApi api = await IsolatedApi.StartAsync(_server, _broker, configure);
        _apis.Add(api);
        return api;
    }
}
