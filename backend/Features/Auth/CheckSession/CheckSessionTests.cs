using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Infrastructure.Persistence;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Auth.CheckSession;

public sealed class CheckSessionTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public CheckSessionTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ActiveSession_AnswersThePermissionsOfItsUser()
    {
        TestSession alice = new(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        Guid session = await SessionIdAsync(alice.AuthId);

        using HttpResponseMessage response = await CheckAsync(alice.AuthId, session);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            ["Permissions.Projects.Create", "Permissions.Tasks.Create"],
            body.RootElement.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));
    }

    [Fact]
    public async Task UnknownClosedOrForeignSession_Answers404()
    {
        TestSession alice = new(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        TestSession bob = new(_fixture.CreateClient());
        await bob.RegisterAsync(TestAccounts.UniqueName("bob"));
        Guid aliceSession = await SessionIdAsync(alice.AuthId);

        using HttpResponseMessage unknown = await CheckAsync(alice.AuthId, Guid.NewGuid());
        using HttpResponseMessage foreign = await CheckAsync(bob.AuthId, aliceSession);
        SessionResponse logout = await alice.SendAsync(HttpMethod.Post, "api/v1/auth/logout");
        logout.Payload?.Dispose();
        using HttpResponseMessage closed = await CheckAsync(alice.AuthId, aliceSession);

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, logout.Status);
        Assert.Equal(HttpStatusCode.NotFound, closed.StatusCode);
    }

    private async Task<HttpResponseMessage> CheckAsync(string userId, Guid sessionId)
    {
        using HttpClient monolith = _fixture.CreateMonolithClient();
        return await monolith.PostAsJsonAsync(
            "internal/v1/session-checks",
            new { userId, sessionId },
            TestContext.Current.CancellationToken);
    }

    private async Task<Guid> SessionIdAsync(string authId)
    {
        using IServiceScope scope = _fixture.Services.CreateScope();
        PlatformDbContext db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        return await db.Database
            .SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM auth.\"Sessions\" WHERE \"UserId\" = {authId}")
            .SingleAsync(TestContext.Current.CancellationToken);
    }
}
