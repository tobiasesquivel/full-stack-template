using Npgsql;
using TemplateApp.Tests.Support.Hosts;
using Xunit;

namespace TemplateApp.Tests.Persistence;

public sealed class TestDatabaseCapacityTests
{
    private readonly PostgresFixture _postgres;

    public TestDatabaseCapacityTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task TheTestDatabase_AcceptsAPoolPerHostForEverySuiteHost()
    {
        await using NpgsqlConnection connection = await TestDatabases.OpenAsync(_postgres.ConnectionString);
        await using NpgsqlCommand show = new("SHOW max_connections", connection);

        Assert.Equal(PostgresFixture.MaxConnections.ToString(System.Globalization.CultureInfo.InvariantCulture), await show.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }
}
