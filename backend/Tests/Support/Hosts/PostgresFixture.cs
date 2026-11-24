using Testcontainers.PostgreSql;
using TemplateApp.Tests.Support.Hosts;
using Xunit;

[assembly: AssemblyFixture(typeof(PostgresFixture))]

namespace TemplateApp.Tests.Support.Hosts;

public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;

    public string ConnectionString { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        string? external = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION");
        if (!string.IsNullOrWhiteSpace(external))
        {
            ConnectionString = external;
            return;
        }

        _postgres = new PostgreSqlBuilder("postgres:18")
            .WithDatabase("templateapp_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await _postgres.StartAsync();
        ConnectionString = _postgres.GetConnectionString();
    }

    public async ValueTask DisposeAsync()
    {
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }
}
