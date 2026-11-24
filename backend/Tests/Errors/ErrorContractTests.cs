using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TemplateApp.Tests.Support;
using TemplateApp.Tests.Support.Clients;
using TemplateApp.Tests.Support.Hosts;
using TemplateApp.Tests.Support.Probes;
using Xunit;

namespace TemplateApp.Tests.Errors;

public sealed class ErrorContractTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public ErrorContractTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task InvalidBody_Returns400_ValidationFailed_WithFieldErrors()
    {
        TestSession alice = new(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        SessionResponse response = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "   " });

        JsonElement problem = ProblemAssert.Problem(response, HttpStatusCode.BadRequest, "Validation.Failed");
        Assert.True(problem.GetProperty("errors").TryGetProperty("Title", out _));
    }

    [Fact]
    public async Task InvalidQuery_Returns400_ValidationFailed_WithFieldErrors()
    {
        TestSession alice = new(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        SessionResponse response = await alice.SendAsync(HttpMethod.Get, "api/v1/tasks?pageSize=101", csrf: false);

        JsonElement problem = ProblemAssert.Problem(response, HttpStatusCode.BadRequest, "Validation.Failed");
        Assert.True(problem.GetProperty("errors").TryGetProperty("PageSize", out _));
    }

    [Fact]
    public async Task MissingAntiforgeryToken_Returns400_AntiforgeryFailed_WithoutFieldErrors()
    {
        TestSession anonymous = new(_fixture.CreateClient());

        SessionResponse response = await anonymous.SendAsync(
            HttpMethod.Post,
            "api/v1/auth/login",
            new { userName = "nobody", password = "wrong-password" },
            csrf: false);

        JsonElement problem = ProblemAssert.Problem(response, HttpStatusCode.BadRequest, "Auth.AntiforgeryFailed");
        Assert.False(problem.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task BadCredentials_Return401_InvalidCredentials()
    {
        TestSession attempt = new(_fixture.CreateClient());
        await attempt.CsrfAsync();

        SessionResponse response = await attempt.SendAsync(
            HttpMethod.Post,
            "api/v1/auth/login",
            new { userName = TestAccounts.UniqueName("ghost"), password = "wrong-password" });

        ProblemAssert.Problem(response, HttpStatusCode.Unauthorized, "Auth.InvalidCredentials");
    }

    [Fact]
    public async Task NoSession_Returns401_SessionRequired()
    {
        TestSession anonymous = new(_fixture.CreateClient());

        SessionResponse response = await anonymous.SendAsync(HttpMethod.Get, "api/v1/tasks", csrf: false);

        ProblemAssert.Problem(response, HttpStatusCode.Unauthorized, "Auth.SessionRequired");
    }

    [Fact]
    public async Task ForeignTask_Returns403_TaskForbidden()
    {
        TestSession alice = new(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        TestSession bob = new(_fixture.CreateClient());
        await bob.RegisterAsync(TestAccounts.UniqueName("bob"));
        string id = await alice.CreateTaskAsync("Ajena");

        SessionResponse response = await bob.SendAsync(HttpMethod.Get, $"api/v1/tasks/{id}", csrf: false);

        ProblemAssert.Problem(response, HttpStatusCode.Forbidden, "Task.Forbidden");
    }

    [Fact]
    public async Task ForeignUser_Returns403_UserForbidden()
    {
        TestSession alice = new(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        TestSession bob = new(_fixture.CreateClient());
        await bob.RegisterAsync(TestAccounts.UniqueName("bob"));

        SessionResponse response = await bob.SendAsync(HttpMethod.Get, $"api/v1/users/{alice.AuthId}", csrf: false);

        ProblemAssert.Problem(response, HttpStatusCode.Forbidden, "User.Forbidden");
    }

    [Fact]
    public async Task CookieForbid_Returns403_AccessDenied()
    {
        TestSession alice = new(_fixture.CreateIsolatedClient(AddErrorProbe, out _));
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        SessionResponse response = await alice.SendAsync(HttpMethod.Get, ErrorProbe.ForbidPath.TrimStart('/'), csrf: false);

        ProblemAssert.Problem(response, HttpStatusCode.Forbidden, "Auth.AccessDenied");
    }

    [Fact]
    public async Task MissingTask_Returns404_TaskNotFound()
    {
        TestSession alice = new(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        SessionResponse response = await alice.SendAsync(HttpMethod.Get, $"api/v1/tasks/{Guid.NewGuid():D}", csrf: false);

        ProblemAssert.Problem(response, HttpStatusCode.NotFound, "Task.NotFound");
    }

    [Fact]
    public async Task DuplicateEmail_Returns409_EmailAlreadyExists()
    {
        TestSession alice = new(_fixture.CreateClient());
        string name = TestAccounts.UniqueName("alice");
        await alice.RegisterAsync(name);
        TestSession copy = new(_fixture.CreateClient());
        await copy.CsrfAsync();

        string other = TestAccounts.UniqueName("copy");
        SessionResponse response = await copy.SendAsync(
            HttpMethod.Post,
            "api/v1/auth/register",
            new { userName = other, email = $"{name}@example.test", password = $"Smoke!{Guid.NewGuid():N}Aa9", displayName = other });

        ProblemAssert.Problem(response, HttpStatusCode.Conflict, "Auth.EmailAlreadyExists");
    }

    [Fact]
    public async Task UnknownRoute_Returns404_HttpNotFound()
    {
        TestSession anonymous = new(_fixture.CreateClient());

        SessionResponse response = await anonymous.SendAsync(HttpMethod.Get, "nope", csrf: false);

        ProblemAssert.Problem(response, HttpStatusCode.NotFound, "Http.NotFound");
    }

    [Fact]
    public async Task IdThatIsNotAGuid_Returns404_HttpNotFound()
    {
        TestSession alice = new(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        SessionResponse response = await alice.SendAsync(HttpMethod.Get, "api/v1/tasks/no-guid", csrf: false);

        ProblemAssert.Problem(response, HttpStatusCode.NotFound, "Http.NotFound");
    }

    [Fact]
    public async Task WrongMethod_Returns405_HttpMethodNotAllowed()
    {
        TestSession alice = new(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        SessionResponse response = await alice.SendAsync(HttpMethod.Patch, "api/v1/tasks");

        ProblemAssert.Problem(response, HttpStatusCode.MethodNotAllowed, "Http.MethodNotAllowed");
    }

    [Fact]
    public async Task PlainTextBody_Returns415_HttpUnsupportedMediaType()
    {
        TestSession alice = new(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        SessionResponse response = await alice.SendRawAsync(HttpMethod.Post, "api/v1/tasks", "hola", "text/plain");

        ProblemAssert.Problem(response, HttpStatusCode.UnsupportedMediaType, "Http.UnsupportedMediaType");
    }

    [Fact]
    public async Task BrokenJson_Returns400_HttpBadRequest()
    {
        TestSession alice = new(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        SessionResponse response = await alice.SendRawAsync(HttpMethod.Post, "api/v1/tasks", "{\"title\":", "application/json");

        ProblemAssert.Problem(response, HttpStatusCode.BadRequest, "Http.BadRequest");
    }

    [Fact]
    public async Task AuthLimiterRejection_Returns429_HttpTooManyRequests_WithRetryAfter()
    {
        TestSession anonymous = new(_fixture.CreateIsolatedClient(_ => { }, out _));
        await anonymous.CsrfAsync();
        SessionResponse response = await anonymous.SendAsync(HttpMethod.Post, "api/v1/auth/logout");
        for (int attempt = 0; attempt < 20 && response.Status != HttpStatusCode.TooManyRequests; attempt++)
        {
            response.Payload?.Dispose();
            response = await anonymous.SendAsync(HttpMethod.Post, "api/v1/auth/logout");
        }

        ProblemAssert.Problem(response, HttpStatusCode.TooManyRequests, "Http.TooManyRequests");
        Assert.True(int.TryParse(Assert.Contains("Retry-After", response.Headers), out int seconds) && seconds > 0);
    }

    [Fact]
    public async Task UnhandledException_Returns500_Unexpected_WithoutInternals_And_IsLogged()
    {
        CapturedLogs logs = new();
        TestSession anonymous = new(_fixture.CreateIsolatedClient(
            services =>
            {
                AddErrorProbe(services);
                services.AddSingleton<ILoggerFactory>(new LoggerFactory([logs]));
            },
            out _));

        SessionResponse response = await anonymous.SendAsync(HttpMethod.Get, ErrorProbe.ThrowPath.TrimStart('/'), csrf: false);

        JsonElement problem = ProblemAssert.Problem(response, HttpStatusCode.InternalServerError, "Http.Unexpected");
        Assert.False(problem.TryGetProperty("detail", out _));
        Assert.DoesNotContain(ErrorProbe.SecretMessage, response.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(InvalidOperationException), response.Text, StringComparison.Ordinal);
        Assert.Contains(logs.Entries, entry =>
            entry.Level >= LogLevel.Error && entry.Exception?.Message == ErrorProbe.SecretMessage);
    }

    private static void AddErrorProbe(IServiceCollection services) =>
        services.AddSingleton<IStartupFilter, ErrorProbe>();
}
