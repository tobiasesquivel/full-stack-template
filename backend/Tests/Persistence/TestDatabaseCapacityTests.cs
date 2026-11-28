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

        int maxConnections = int.Parse((string)(await show.ExecuteScalarAsync(TestContext.Current.CancellationToken))!, System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(maxConnections >= PostgresFixture.MaxConnections, $"The test database accepts {maxConnections} connections; the suite needs at least {PostgresFixture.MaxConnections} (a pool per host and connection string).");
    }
}
