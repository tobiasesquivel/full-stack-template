using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Tests.Support;
using TemplateApp.Tests.Support.Clients;
using TemplateApp.Tests.Support.Hosts;
using TemplateApp.Tests.Support.Probes;
using Xunit;

namespace TemplateApp.Tests.RateLimiting;

public sealed class RateLimitingTests : IClassFixture<ApiFixture>
{
    private const int WindowSeconds = 3600;

    private readonly ApiFixture _fixture;

    public RateLimitingTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task UserWrites_OverTheLimit_Return429_WithRetryAfter()
    {
        HttpClient client = Isolated(("RateLimiting:UserWrites", "2"));
        TestSession alice = new(client);
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        await alice.CreateTaskAsync("first");
        await alice.CreateTaskAsync("second");
        SessionResponse rejected = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "third" });

        AssertRejected(rejected);
    }

    [Fact]
    public async Task ExhaustedWrites_LeaveTheUsersReads_AndOtherUsers_Alone()
    {
        HttpClient client = Isolated(("RateLimiting:UserWrites", "1"));
        TestSession alice = new(client);
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        await alice.CreateTaskAsync("first");
        AssertRejected(await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "second" }));

        SessionResponse reads = await alice.SendAsync(HttpMethod.Get, "api/v1/tasks", csrf: false);
        reads.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, reads.Status);

        TestSession bob = new(client);
        await bob.RegisterAsync(TestAccounts.UniqueName("bob"));
        await bob.CreateTaskAsync("bob's first");
    }

    [Fact]
    public async Task UserReads_OverTheLimit_Return429_WithRetryAfter()
    {
        HttpClient client = Isolated(("RateLimiting:UserReads", "2"));
        TestSession alice = new(client);
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        for (int read = 0; read < 2; read++)
        {
            SessionResponse allowed = await alice.SendAsync(HttpMethod.Get, "api/v1/tasks", csrf: false);
            allowed.Payload?.Dispose();
            Assert.Equal(HttpStatusCode.OK, allowed.Status);
        }

        AssertRejected(await alice.SendAsync(HttpMethod.Get, "api/v1/tasks", csrf: false));
    }

    [Fact]
    public async Task AnonymousRequests_ArePartitionedByClientAddress()
    {
        HttpClient client = Isolated(("RateLimiting:AnonymousRequests", "2"));
        TestSession first = new(client);
        Dictionary<string, string> firstAddress = new() { [ClientAddressProbe.Header] = "203.0.113.10" };
        Dictionary<string, string> secondAddress = new() { [ClientAddressProbe.Header] = "203.0.113.20" };

        for (int request = 0; request < 2; request++)
        {
            SessionResponse allowed = await first.SendAsync(HttpMethod.Get, "api/v1/auth/csrf", csrf: false, headers: firstAddress);
            Assert.Equal(HttpStatusCode.OK, allowed.Status);
        }

        AssertRejected(await first.SendAsync(HttpMethod.Get, "api/v1/auth/csrf", csrf: false, headers: firstAddress));

        SessionResponse other = await new TestSession(client).SendAsync(HttpMethod.Get, "api/v1/auth/csrf", csrf: false, headers: secondAddress);
        Assert.Equal(HttpStatusCode.OK, other.Status);
    }

    [Fact]
    public async Task AuthPolicy_IsPartitionedByClientAddress()
    {
        HttpClient client = Isolated(("AuthRateLimiting:PermitLimit", "2"));
        Dictionary<string, string> firstAddress = new() { [ClientAddressProbe.Header] = "203.0.113.30" };
        Dictionary<string, string> secondAddress = new() { [ClientAddressProbe.Header] = "203.0.113.40" };
        object wrongCredentials = new { userName = TestAccounts.UniqueName("ghost"), password = "wrong-password" };

        TestSession first = new(client);
        await first.CsrfAsync();
        for (int attempt = 0; attempt < 2; attempt++)
        {
            SessionResponse refused = await first.SendAsync(HttpMethod.Post, "api/v1/auth/login", wrongCredentials, headers: firstAddress);
            ProblemAssert.Problem(refused, HttpStatusCode.Unauthorized, "Auth.InvalidCredentials");
        }

        AssertRejected(await first.SendAsync(HttpMethod.Post, "api/v1/auth/login", wrongCredentials, headers: firstAddress));

        TestSession second = new(client);
        await second.CsrfAsync();
        SessionResponse other = await second.SendAsync(HttpMethod.Post, "api/v1/auth/login", wrongCredentials, headers: secondAddress);
        ProblemAssert.Problem(other, HttpStatusCode.Unauthorized, "Auth.InvalidCredentials");
    }

    [Fact]
    public async Task HealthEndpoints_AreExempt()
    {
        HttpClient client = Isolated(("RateLimiting:AnonymousRequests", "1"));
        TestSession probe = new(client);

        for (int request = 0; request < 3; request++)
        {
            SessionResponse live = await probe.SendAsync(HttpMethod.Get, "api/v1/health/live", csrf: false);
            live.Payload?.Dispose();
            Assert.Equal(HttpStatusCode.OK, live.Status);
        }
    }

    private HttpClient Isolated((string Key, string Value) limit)
    {
        Dictionary<string, string> settings = new()
        {
            ["RateLimiting:WindowSeconds"] = WindowSeconds.ToString(CultureInfo.InvariantCulture),
            ["AuthRateLimiting:WindowSeconds"] = WindowSeconds.ToString(CultureInfo.InvariantCulture),
            [limit.Key] = limit.Value,
        };
        return _fixture.CreateIsolatedClient(
            settings,
            services => services.AddSingleton<IStartupFilter, ClientAddressProbe>(),
            out _);
    }

    private static void AssertRejected(SessionResponse response)
    {
        ProblemAssert.Problem(response, HttpStatusCode.TooManyRequests, "Http.TooManyRequests");
        string retryAfter = Assert.Contains("Retry-After", response.Headers);
        int seconds = int.Parse(retryAfter, NumberStyles.None, CultureInfo.InvariantCulture);
        Assert.InRange(seconds, 1, WindowSeconds);
    }
}
