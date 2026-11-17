using System.Net;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Tasks.Contracts;
using TemplateApp.Features.Tasks.Domain;
using TemplateApp.Infrastructure.Persistence;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Tasks.GetTask;

public sealed class GetTaskComparisonTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public GetTaskComparisonTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DedicatedRead_MatchesTheEntityRead_InOneQuery_WithoutTrackingTheTask()
    {
        CapturedCommands commands = new();
        HttpClient client = _fixture.CreateIsolatedClient(
            services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(commands)),
            out IServiceProvider services);

        var alice = new TestSession(client);
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        var carol = new TestSession(client);
        await carol.RegisterAsync(TestAccounts.UniqueName("carol"));
        var admin = new TestSession(client);
        string adminName = TestAccounts.UniqueName("admin");
        await admin.RegisterAsync(adminName);
        await _fixture.SetRoleAsync(adminName, "Admin", member: true);

        string open = await alice.CreateTaskAsync("Abierta");
        string completed = await alice.CreateTaskAsync("Completada");
        await PutAsync(alice, completed, "Completada", isCompleted: true);
        string reopened = await alice.CreateTaskAsync("Reabierta");
        await PutAsync(alice, reopened, "Reabierta", isCompleted: true);
        await PutAsync(alice, reopened, "Reabierta", isCompleted: false);
        string renamed = await alice.CreateTaskAsync("Nombre viejo");
        await PutAsync(alice, renamed, "Nombre nuevo", isCompleted: false);
        string deleted = await alice.CreateTaskAsync("Borrada");
        var removal = await alice.SendAsync(HttpMethod.Delete, $"api/v1/tasks/{deleted}");
        removal.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.NoContent, removal.Status);

        (string Name, string CallerId, Guid Id)[] cases =
        [
            ("propia abierta", alice.AuthId, Guid.Parse(open)),
            ("propia completada", alice.AuthId, Guid.Parse(completed)),
            ("propia reabierta", alice.AuthId, Guid.Parse(reopened)),
            ("propia renombrada", alice.AuthId, Guid.Parse(renamed)),
            ("ajena sin permiso", carol.AuthId, Guid.Parse(open)),
            ("ajena como Admin", admin.AuthId, Guid.Parse(completed)),
            ("inexistente", alice.AuthId, Guid.NewGuid()),
            ("borrada", alice.AuthId, Guid.Parse(deleted)),
        ];

        foreach ((string name, string callerId, Guid id) in cases)
        {
            ErrorOr<TaskResponse> control;
            using (IServiceScope scope = services.CreateScope())
                control = await EntityRead(scope.ServiceProvider, callerId, id);

            using (IServiceScope scope = services.CreateScope())
            {
                commands.Reset();
                ErrorOr<TaskResponse> candidate = await scope.ServiceProvider.GetRequiredService<GetTask>()
                    .ExecuteAsync(callerId, id, TestContext.Current.CancellationToken);

                Assert.True(control.IsError == candidate.IsError, $"{name}: one read failed and the other did not");
                if (control.IsError)
                {
                    Assert.Equal((control.FirstError.Type, control.FirstError.Code), (candidate.FirstError.Type, candidate.FirstError.Code));
                }
                else
                {
                    Assert.Equal(control.Value, candidate.Value);
                }

                Assert.Single(commands.Reading("TaskItems"));
                AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                Assert.True(!db.ChangeTracker.Entries<TaskItem>().Any(), $"{name}: the read left a TaskItem tracked");
            }
        }
    }

    private static async Task<ErrorOr<TaskResponse>> EntityRead(IServiceProvider provider, string callerId, Guid id)
    {
        AppDbContext db = provider.GetRequiredService<AppDbContext>();
        IAuthModule auth = provider.GetRequiredService<IAuthModule>();
        CancellationToken ct = TestContext.Current.CancellationToken;

        var item = await db.TaskItems.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (item is null)
            return TaskErrors.NotFound(id);

        if (!await auth.CanAccessAsync(callerId, item, TaskPermissions.Read, ct))
            return TaskErrors.Forbidden;

        return TaskResponses.From(item);
    }

    private static async Task PutAsync(TestSession session, string id, string title, bool isCompleted)
    {
        uint version = await session.TaskVersionAsync(id);
        var updated = await session.SendAsync(HttpMethod.Put, $"api/v1/tasks/{id}", new { title, isCompleted, version });
        updated.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, updated.Status);
    }
}
