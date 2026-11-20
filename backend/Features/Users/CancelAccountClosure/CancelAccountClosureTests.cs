using System.Text.Json;
using TemplateApp.Features.Users.DeleteUser;
using TemplateApp.Tests.Messaging;
using TemplateApp.Tests.Support;
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
    public async Task TheSameRejectionTwice_CompensatesEachParticipantOnce()
    {
        IsolatedApi api = await IsolatedApi.StartAsync(_server, _rabbit.Uri);
        _apis.Add(api);
        TaskEventsBroker broker = await TaskEventsBroker.ConnectAsync(_rabbit, api.Prefix);
        _brokers.Add(broker);
        using RecordedMeasurements finished = new(api.Pair.Monolith.Services, "TemplateApp.Users", "templateapp.account_closures.finished");
        TestSession alice = await api.SignUpAsync("alice");
        (_, Guid closure) = await DeleteUserTests.RequestClosureAsync(alice, "api/v1/users/me");
        await api.PublishUsersOutboxAsync();
        Assert.Equal(1, await Eventually.Async(() => DeleteUserBrokerTests.HoldsAsync(api, alice.AuthId), holds => holds == 1, Wait));
        Guid message = Guid.NewGuid();
        Guid marker = Guid.NewGuid();

        await broker.PublishClosureRejectedAsync(message, closure, alice.AuthId);
        await broker.PublishClosureRejectedAsync(message, closure, alice.AuthId);
        await broker.PublishClosureRejectedAsync(marker, Guid.NewGuid(), $"marker-{marker:N}");
        Assert.Equal(2, await Eventually.Async(() => InboxRowsAsync(api, marker), rows => rows == 2, Wait));

        Assert.Equal(2, await InboxRowsAsync(api, message));
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

    private static Task<long> InboxRowsAsync(IsolatedApi api, Guid messageId) =>
        api.Pair.Monolith.Services.CountAsync(
            $"""
            SELECT (SELECT count(*) FROM users."InboxMessages" WHERE "MessageId" = {messageId} AND "Consumer" = 'users.account-closures')
                 + (SELECT count(*) FROM tasks."InboxMessages" WHERE "MessageId" = {messageId} AND "Consumer" = 'tasks.account-closures') AS "Value"
            """);
}
