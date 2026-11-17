using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using TemplateApp.Features.Tasks.Domain;
using TemplateApp.Infrastructure.Persistence;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Tests.Auditing;

public sealed class AuditTrailTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public AuditTrailTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Update_RecordsWhoChangedWhichPropertiesAndWhen()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        var client = _fixture.CreateIsolatedClient(services => services.AddSingleton<TimeProvider>(clock), out IServiceProvider services);
        var alice = new TestSession(client);
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        string id = await alice.CreateTaskAsync("Borrador");
        uint version = await alice.TaskVersionAsync(id);
        clock.Advance(TimeSpan.FromMinutes(5));
        DateTimeOffset updatedAt = clock.GetUtcNow();

        var updated = await alice.SendAsync(HttpMethod.Put, $"api/v1/tasks/{id}", new { title = "Estado final", isCompleted = true, version });
        updated.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, updated.Status);

        List<AuditRow> rows = await services.AuditEntriesAsync(id);
        Assert.Collection(
            rows,
            added =>
            {
                Assert.Equal(("TaskItem", "Added", "user", alice.AuthId, ""), (added.EntityType, added.Operation, added.ActorKind, added.ActorId, added.ChangedProperties));
                Assert.Equal(updatedAt.AddMinutes(-5), added.OccurredAt);
            },
            modified =>
            {
                Assert.Equal(("TaskItem", "Modified", "user", alice.AuthId), (modified.EntityType, modified.Operation, modified.ActorKind, modified.ActorId));
                Assert.Equal("CompletedAt,IsCompleted,Title,UpdatedAt", modified.ChangedProperties);
                Assert.Equal(updatedAt, modified.OccurredAt);
                Assert.Matches("^[0-9a-f]{32}$", modified.TraceId);
            });
    }

    [Fact]
    public async Task AuditTrail_CarriesNoPersonalData()
    {
        var alice = new TestSession(_fixture.CreateClient());
        string userName = TestAccounts.UniqueName("alice");
        await alice.RegisterAsync(userName);

        string displayName = $"Zoe-Sentinel-{Guid.NewGuid():N}";
        const string timezone = "America/Argentina/Buenos_Aires";
        var profile = await alice.SendAsync(HttpMethod.Put, "api/v1/users/me", new { displayName, timezone });
        profile.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, profile.Status);

        string firstTitle = $"Turno medico {Guid.NewGuid():N}";
        string secondTitle = $"Turno dentista {Guid.NewGuid():N}";
        string id = await alice.CreateTaskAsync(firstTitle);
        uint version = await alice.TaskVersionAsync(id);
        var renamed = await alice.SendAsync(HttpMethod.Put, $"api/v1/tasks/{id}", new { title = secondTitle, isCompleted = false, version });
        renamed.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, renamed.Status);

        List<AuditRow> profileRows = await _fixture.Services.AuditEntriesAsync(alice.AuthId);
        Assert.Equal(
            [("UserProfile", "Added", "anonymous", null, ""), ("UserProfile", "Modified", "user", alice.AuthId, "DisplayName,Timezone")],
            profileRows.Select(r => (r.EntityType, r.Operation, r.ActorKind, r.ActorId, r.ChangedProperties)));
        List<AuditRow> taskRows = await _fixture.Services.AuditEntriesAsync(id);
        Assert.Equal(["Added", "Modified"], taskRows.Select(r => r.Operation));

        string dump = await _fixture.Services.AuditDumpAsync(alice.AuthId, alice.AuthId, id);
        Assert.Contains(id, dump);
        foreach (string personal in new[] { userName, $"{userName}@example.test", displayName, timezone, firstTitle, secondTitle })
            Assert.DoesNotContain(personal, dump, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StaleUpdate_LeavesOnlyTheWinnersRow()
    {
        var tabA = new TestSession(_fixture.CreateClient());
        await tabA.RegisterAsync(TestAccounts.UniqueName("alice"));
        var tabB = tabA.CopyTo(_fixture.CreateClient());

        string id = await tabA.CreateTaskAsync("Comprar pan");
        uint read = await tabA.TaskVersionAsync(id);

        var renamed = await tabA.SendAsync(HttpMethod.Put, $"api/v1/tasks/{id}", new { title = "Comprar pan integral", isCompleted = false, version = read });
        renamed.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, renamed.Status);
        var stale = await tabB.SendAsync(HttpMethod.Put, $"api/v1/tasks/{id}", new { title = "Comprar pan", isCompleted = true, version = read });
        ProblemAssert.Problem(stale, HttpStatusCode.Conflict, "Task.Conflict");

        List<AuditRow> rows = await _fixture.Services.AuditEntriesAsync(id);
        Assert.Equal([("Added", ""), ("Modified", "Title,UpdatedAt")], rows.Select(r => (r.Operation, r.ChangedProperties)));
    }

    [Fact]
    public async Task IdempotentCreate_RecordsOneRow_AndNoneForARejectedRequest()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        var rejected = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "" }, headers: KeyHeader(Guid.NewGuid()));
        rejected.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.BadRequest, rejected.Status);

        Dictionary<string, string> key = KeyHeader(Guid.NewGuid());
        var created = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "Comprar pan" }, headers: key);
        created.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Created, created.Status);
        var replayed = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "Comprar pan" }, headers: key);
        replayed.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Created, replayed.Status);

        List<AuditRow> rows = await _fixture.Services.AuditEntriesByActorAsync(alice.AuthId);
        Assert.Equal([("TaskItem", "Added")], rows.Select(r => (r.EntityType, r.Operation)));
    }

    [Fact]
    public async Task AccountDeletion_RecordsTheProfile_ButNotTheCascadedTasks()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        string id = await alice.CreateTaskAsync("Comprar pan");

        var deleted = await alice.SendAsync(HttpMethod.Delete, "api/v1/users/me");
        deleted.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.NoContent, deleted.Status);

        List<AuditRow> profileRows = await _fixture.Services.AuditEntriesAsync(alice.AuthId);
        Assert.Equal(
            [("Added", "anonymous", null), ("Deleted", "user", alice.AuthId)],
            profileRows.Select(r => (r.Operation, r.ActorKind, r.ActorId)));
        List<AuditRow> taskRows = await _fixture.Services.AuditEntriesAsync(id);
        Assert.Equal(["Added"], taskRows.Select(r => r.Operation));
    }

    [Fact]
    public async Task WriteOutsideARequest_IsRecordedAsTheSystem()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        string id = await alice.CreateTaskAsync("Comprar pan");

        using (IServiceScope scope = _fixture.Services.CreateScope())
        {
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            TaskItem item = await db.TaskItems.SingleAsync(t => t.Id == Guid.Parse(id));
            item.IsCompleted = true;
            await db.SaveChangesAsync();
        }

        AuditRow system = (await _fixture.Services.AuditEntriesAsync(id)).Last();
        Assert.Equal(("Modified", "system", null, "IsCompleted"), (system.Operation, system.ActorKind, system.ActorId, system.ChangedProperties));
    }

    private static Dictionary<string, string> KeyHeader(Guid key) => new() { ["Idempotency-Key"] = key.ToString() };
}
