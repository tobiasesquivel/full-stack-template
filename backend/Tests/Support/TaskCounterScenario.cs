using System.Net;
using TemplateApp.Tests.Support.Clients;
using TemplateApp.Tests.Support.Hosts;
using Xunit;

namespace TemplateApp.Tests.Support;

internal static class TaskCounterScenario
{
    public static async Task<IReadOnlyList<TestSession>> SeedAsync(IsolatedApi api)
    {
        TestSession alice = await api.SignUpAsync("alice");
        TestSession bob = await api.SignUpAsync("bob");
        string home = await alice.CreateProjectAsync("Casa");
        await alice.CreateProjectAsync("Vacío");
        string work = await bob.CreateProjectAsync("Trabajo");
        await alice.CreateTaskAsync("Abierta", home);
        string done = await alice.CreateTaskAsync("Hecha", home);
        string reopened = await alice.CreateTaskAsync("Reabierta", home);
        string gone = await bob.CreateTaskAsync("Borrada", work);
        await bob.CreateTaskAsync("Otra", work);
        await alice.CreateTaskAsync("Suelta");
        await SetCompletedAsync(alice, done, true);
        await SetCompletedAsync(alice, reopened, true);
        await SetCompletedAsync(alice, reopened, false);
        await SetCompletedAsync(bob, gone, true);
        SessionResponse deleted = await bob.SendAsync(HttpMethod.Delete, $"api/v1/tasks/{gone}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.Status);
        return [alice, bob];
    }

    public static async Task SetCompletedAsync(TestSession session, string taskId, bool isCompleted)
    {
        uint version = await session.TaskVersionAsync(taskId);
        SessionResponse updated = await session.SendAsync(HttpMethod.Put, $"api/v1/tasks/{taskId}", new { title = "Tarea", isCompleted, version });
        updated.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, updated.Status);
    }
}
