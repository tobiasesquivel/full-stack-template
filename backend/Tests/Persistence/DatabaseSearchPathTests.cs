using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TemplateApp.Infrastructure.Persistence;
using TemplateApp.Tests.Support.Contexts;
using TemplateApp.Tests.Support.Hosts;
using Xunit;

namespace TemplateApp.Tests.Persistence;

public sealed class DatabaseSearchPathTests : IClassFixture<ApiFixture>
{
    private const string MonolithSearchPath = "public,auth,platform,projects,users";
    private const string TasksSearchPath = "public,platform,tasks";

    private readonly ApiFixture _fixture;
    private readonly PostgresFixture _postgres;

    public DatabaseSearchPathTests(ApiFixture fixture, PostgresFixture postgres)
    {
        _fixture = fixture;
        _postgres = postgres;
    }

    [Fact]
    public async Task EveryContextSession_SearchesPublicAndTheSchemaOfEveryContextOfItsHost()
    {
        Assert.Equal(Enumerable.Repeat(MonolithSearchPath, 5), await SessionSearchPathsAsync(_fixture.Services));
        Assert.Equal(Enumerable.Repeat(TasksSearchPath, 3), await SessionSearchPathsAsync(_fixture.TasksServices));
    }

    [Fact]
    public async Task ASearchPathFromConfiguration_IsReplacedByTheCodeOne()
    {
        string configured = new NpgsqlConnectionStringBuilder(_postgres.ConnectionString) { SearchPath = "public" }.ConnectionString;
        ApiPair api = _fixture.CreateIsolatedApi(
            new Dictionary<string, string> { ["ConnectionStrings:DefaultConnection"] = configured },
            TestDatabases.SkipInitialization);

        Assert.Equal(Enumerable.Repeat(MonolithSearchPath, 5), await SessionSearchPathsAsync(api.Monolith.Services));
        Assert.Equal(Enumerable.Repeat(TasksSearchPath, 3), await SessionSearchPathsAsync(api.Tasks.Services));
    }

    [Fact]
    public void TheRegisteredSchemaOfEveryContext_IsItsDefaultSchema_AndIsInTheSearchPath()
    {
        foreach (IServiceProvider host in new[] { _fixture.Services, _fixture.TasksServices })
        {
            using IServiceScope scope = host.CreateScope();
            string[] searchPath = DatabaseSearchPath.Of(host).Split(',');

            Assert.All(
                host.GetServices<MigratedContext>(),
                migrated =>
                {
                    DbContext context = (DbContext)scope.ServiceProvider.GetRequiredService(migrated.ContextType);
                    Assert.Equal(migrated.Schema, context.Model.GetDefaultSchema());
                    Assert.Contains(migrated.Schema, searchPath);
                });
        }
    }

    [Theory]
    [InlineData("ProbeData")]
    [InlineData("probe-data")]
    [InlineData("1probe")]
    [InlineData("")]
    public void ASchemaThatPostgreSqlWouldFoldOrNeedQuotes_IsRejectedWhenTheContextIsRegistered(string schema) =>
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddMigratedDbContext<ProbeDbContext>(schema, adoptsLegacyTables: false));

    [Fact]
    public void APlainLowercaseSchema_IsRegistered() =>
        Assert.Contains(
            new ServiceCollection().AddMigratedDbContext<ProbeDbContext>("probe_2", adoptsLegacyTables: false),
            descriptor => descriptor.ImplementationInstance is MigratedContext { Schema: "probe_2" });

    private static async Task<string[]> SessionSearchPathsAsync(IServiceProvider services)
    {
        using IServiceScope scope = services.CreateScope();
        DbContext[] contexts = [scope.ServiceProvider.GetRequiredService<AppDbContext>(), .. scope.ServiceProvider.MigratedContexts()];
        List<string> paths = [];
        foreach (DbContext context in contexts)
            paths.Add(await context.Database.SqlQuery<string>($"SELECT current_setting('search_path') AS \"Value\"").SingleAsync());
        return [.. paths];
    }
}
