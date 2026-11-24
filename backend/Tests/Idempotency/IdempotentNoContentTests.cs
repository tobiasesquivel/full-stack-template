using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TemplateApp.Features.Users.DeleteUser;
using TemplateApp.Tests.Support.Clients;
using TemplateApp.Tests.Support.Hosts;
using TemplateApp.Tests.Support.Probes;
using Xunit;

namespace TemplateApp.Tests.Idempotency;

public sealed class IdempotentNoContentTests : IDisposable
{
    private readonly string _server;
    private readonly Uri _broker;
    private readonly List<IsolatedApi> _apis = [];

    public IdempotentNoContentTests(PostgresFixture postgres, RabbitMqFixture rabbit)
    {
        _server = postgres.ConnectionString;
        _broker = rabbit.Uri;
    }

    [Fact]
    public async Task OnKestrel_A204WithAnIdempotencyKey_AndItsReplay_FinishWithoutAServerError_AndTheConnectionKeepsServing()
    {
        CapturedLogs logs = new();
        IsolatedApi api = await IsolatedApi.StartAsync(
            _server,
            _broker,
            services => services.AddSingleton<ILoggerFactory>(new LoggerFactory([logs])),
            kestrel: true);
        _apis.Add(api);
        TestSession alice = await api.SignUpAsync("alice");
        (_, Guid closure, _) = await DeleteUserTests.ScheduleClosureAsync(alice, "api/v1/users/me");
        Dictionary<string, string> key = new() { ["Idempotency-Key"] = Guid.NewGuid().ToString() };
        string route = $"api/v1/users/closures/{closure}/cancel";

        SessionResponse cancelled = await alice.SendAsync(HttpMethod.Post, route, headers: key);
        cancelled.Payload?.Dispose();
        SessionResponse replayed = await alice.SendAsync(HttpMethod.Post, route, headers: key);
        replayed.Payload?.Dispose();
        SessionResponse again = await alice.SendAsync(HttpMethod.Post, route);

        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.NoContent), (cancelled.Status, replayed.Status));
        Assert.Equal("true", Assert.Contains("Idempotent-Replayed", replayed.Headers));
        ProblemAssert.Problem(again, HttpStatusCode.Conflict, "User.ClosureNotCancellable");
        Assert.Empty(logs.Entries.Where(entry => entry.Level >= LogLevel.Error && entry.Exception is InvalidOperationException).Select(entry => entry.Message));
    }

    public void Dispose()
    {
        foreach (IsolatedApi api in _apis)
            api.Dispose();
    }
}
