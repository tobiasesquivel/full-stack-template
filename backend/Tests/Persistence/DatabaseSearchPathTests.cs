using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TemplateApp.Infrastructure.Persistence;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Tests.Persistence;

public sealed class DatabaseSearchPathTests : IClassFixture<ApiFixture>
{
    private const string ExpectedSearchPath = "public,auth,users,tasks,platform,projects";

    private readonly ApiFixture _fixture;
    private readonly PostgresFixture _postgres;

    public DatabaseSearchPathTests(ApiFixture fixture, PostgresFixture postgres)
    {
        _fixture = fixture;
        _postgres = postgres;
    }

    [Fact]
    public async Task EveryContextSession_SearchesPublicAndEveryFeatureSchema()
    {
        Assert.Equal(
            Enumerable.Repeat(ExpectedSearchPath, 6),
            await SessionSearchPathsAsync(_fixture.Services));
    }

    [Fact]
    public async Task ASearchPathFromConfiguration_IsReplacedByTheCodeOne()
    {
        string configured = new NpgsqlConnectionStringBuilder(_postgres.ConnectionString) { SearchPath = "public" }.ConnectionString;
        using HttpClient client = _fixture.CreateIsolatedClient(
            new Dictionary<string, string> { ["ConnectionStrings:DefaultConnection"] = configured },
            TestDatabases.SkipInitialization,
            out IServiceProvider services);

        Assert.Equal(Enumerable.Repeat(ExpectedSearchPath, 6), await SessionSearchPathsAsync(services));
    }

    [Fact]
    public void TheSchemaOfEveryContext_IsInTheSearchPath()
    {
        using IServiceScope scope = _fixture.Services.CreateScope();

        Assert.All(
            scope.ServiceProvider.MigratedContexts(),
            context => Assert.Contains(context.Model.GetDefaultSchema(), ExpectedSearchPath.Split(',')));
    }

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
