using System.Text.Json;
using TemplateApp.Features.Users.DeleteUser;
using TemplateApp.Tests.Messaging;
using TemplateApp.Tests.Support;
using TemplateApp.Tests.Support.Clients;
using TemplateApp.Tests.Support.Hosts;
using TemplateApp.Tests.Support.Probes;
using TemplateApp.Tests.Support.Rows;
using Xunit;

namespace TemplateApp.Features.Users.CancelAccountClosure;

public sealed class CancelAccountClosureTests : IAsyncDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);

    private readonly string _server;
    private readonly RabbitMqFixture _rabbit;
    private readonly List<IsolatedApi> _apis = [];
    private readonly List<TaskEventsBroker> _brokers = [];

    public CancelAccountClosureTests(PostgresFixture postgres, RabbitMqFixture rabbit)
    {
        _server = postgres.ConnectionString;
        _rabbit = rabbit;
    }

    [Fact]
    public async Task ARejectionThatArrivesAfterTheClosureEnded_TwiceWithTheSameId_ChangesNothing()
    {
        IsolatedApi api = await IsolatedApi.StartAsync(_server, _rabbit.Uri);
        _apis.Add(api);
        TaskEventsBroker broker = await TaskEventsBroker.ConnectAsync(_rabbit, api.Prefix);
        _brokers.Add(broker);
        using RecordedMeasurements finished = new(api.Pair.Monolith.Services, "TemplateApp.Users", "templateapp.account_closures.finished");
        TestSession alice = await api.SignUpAsync("alice");
        await api.Pair.Monolith.Services.SetRoleAsync(alice.UserName, "SuperAdmin", member: true);
        (_, Guid closure) = await DeleteUserTests.RequestClosureAsync(api, alice, "api/v1/users/me");
        await api.DrainAsync();
        long version = await VersionAsync(api, closure);
        Guid message = Guid.NewGuid();

        await broker.PublishClosureRejectedAsync(message, closure, alice.AuthId);
        await broker.PublishClosureRejectedAsync(message, closure, alice.AuthId);
        Assert.Equal(1, await Eventually.Async(() => HandledAsync(api, message), rows => rows == 1, Wait));
        await api.DrainAsync();

        Assert.Equal("Rejected", await DeleteUserBrokerTests.SagaStateAsync(api, closure));
        Assert.Equal(version, await VersionAsync(api, closure));
        Assert.Equal(0, await DeleteUserBrokerTests.HoldsAsync(api, alice.AuthId));
        JsonElement me = await DeleteUserTests.MeAsync(alice, "api/v1/users/me");
        Assert.Equal("active", me.GetProperty("accountStatus").GetString());
        Assert.Equal(closure, me.GetProperty("lastClosure").GetProperty("closureId").GetGuid());
        Assert.Single(finished.Values);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (TaskEventsBroker broker in _brokers)
            await broker.DisposeAsync();
        foreach (IsolatedApi api in _apis)
            api.Dispose();
    }

    private static Task<long> VersionAsync(IsolatedApi api, Guid closureId) =>
        api.Pair.Monolith.Services.CountAsync($"""SELECT "Version"::bigint AS "Value" FROM users."AccountClosureSagas" WHERE "ClosureId" = {closureId}""");

    private static Task<long> HandledAsync(IsolatedApi api, Guid messageId) =>
        api.Pair.Monolith.Services.CountAsync($"""SELECT count(*) AS "Value" FROM wolverine.wolverine_incoming_envelopes WHERE id = {messageId} AND status = 'Handled'""");
}
