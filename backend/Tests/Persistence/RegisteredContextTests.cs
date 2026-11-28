using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Infrastructure.Persistence;
using TemplateApp.Tests.Support.Contexts;
using TemplateApp.Tests.Support.Hosts;
using TemplateApp.Tests.Support.Rows;
using Xunit;

namespace TemplateApp.Tests.Persistence;

public sealed class RegisteredContextTests : IAsyncDisposable
{
    private readonly string _server;
    private readonly RabbitMqFixture _rabbit;
    private IsolatedApi? _api;

    public RegisteredContextTests(PostgresFixture postgres, RabbitMqFixture rabbit)
    {
        _server = postgres.ConnectionString;
        _rabbit = rabbit;
    }

    [Fact]
    public async Task AContextRegisteredOnlyInTasks_JoinsOnlyItsSearchPath_DrainsItsOutbox_AndCountsItsRowsByOwner()
    {
        _api = await IsolatedApi.StartAsync(_server, _rabbit.Uri, configureTasks: ProbeDbContext.Register);
        CancellationToken ct = TestContext.Current.CancellationToken;
        string holder = Guid.NewGuid().ToString();
        await using (AsyncServiceScope scope = _api.Pair.Tasks.Services.CreateAsyncScope())
        {
            ProbeDbContext probe = scope.ServiceProvider.GetRequiredService<ProbeDbContext>();
            await probe.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(ct);
            probe.Items.AddRange(ProbeItem.Create(holder), ProbeItem.Create(holder), ProbeItem.Create(Guid.NewGuid().ToString()));
            await probe.SaveChangesAsync(ct);
        }

        Assert.Equal("public,auth,platform,projects,users", await SearchPathAsync(_api.Pair.Monolith.Services));
        Assert.Equal("public,platform,probe,tasks", await SearchPathAsync(_api.Pair.Tasks.Services));
        Assert.Equal(2, (await _api.Pair.OwnedRowsAsync(holder))["probe.ProbeItems"]);
        OwnedRows monolithRows = await _api.Pair.Monolith.Services.OwnedRowsAsync(holder);
        Assert.Throws<KeyNotFoundException>(() => monolithRows["probe.ProbeItems"]);
        Assert.Equal(3, await _api.Pair.Monolith.Services.CountAsync($"""SELECT count(*) AS "Value" FROM probe."OutboxMessages" WHERE "ProcessedAt" IS NULL"""));

        await _api.DrainAsync();

        Assert.Equal(0, await _api.Pair.Monolith.Services.CountAsync($"""SELECT count(*) AS "Value" FROM probe."OutboxMessages" WHERE "ProcessedAt" IS NULL"""));
        Assert.Equal(3, await _api.Pair.Monolith.Services.CountAsync($"""SELECT count(*) AS "Value" FROM probe."OutboxMessages" WHERE "ProcessedAt" IS NOT NULL"""));
    }

    private static async Task<string> SearchPathAsync(IServiceProvider host)
    {
        using IServiceScope scope = host.CreateScope();
        PlatformDbContext platform = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        return await platform.Database.SqlQuery<string>($"SELECT current_setting('search_path') AS \"Value\"").SingleAsync();
    }

    public ValueTask DisposeAsync()
    {
        _api?.Dispose();
        return ValueTask.CompletedTask;
    }
}
