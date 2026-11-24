using System.Diagnostics;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TemplateApp.Features.Tasks.Domain;
using TemplateApp.Features.Tasks.Persistence;
using TemplateApp.Tests.Support.Clients;
using TemplateApp.Tests.Support.Faults;
using TemplateApp.Tests.Support.Hosts;
using Xunit;

namespace TemplateApp.Tests.Messaging;

public sealed class MediatorFailureTests : IClassFixture<ApiFixture>
{
    private static readonly TimeSpan NoRetryBudget = TimeSpan.FromSeconds(1);

    private readonly ApiFixture _fixture;

    public MediatorFailureTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DatabaseErrorInsideAHandler_Returns500AtOnce_AndRunsTheHandlerOnce()
    {
        var failure = new FailAfterSaving<TaskItem>(error: () => new NpgsqlException("The database dropped the connection."));
        HttpClient client = _fixture.CreateIsolatedTasksClient(
            services => services.ConfigureDbContext<TasksDbContext>((_, options) => options.AddInterceptors(failure)),
            out _);
        var alice = new TestSession(client);
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        Stopwatch elapsed = Stopwatch.StartNew();
        var failed = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "Comprar pan" });
        elapsed.Stop();
        failed.Payload?.Dispose();

        Assert.Equal(HttpStatusCode.InternalServerError, failed.Status);
        Assert.Equal(1, failure.Failures);
        Assert.True(elapsed.Elapsed < NoRetryBudget, $"The request took {elapsed.Elapsed}: the handler was retried inside the request.");
    }
}
