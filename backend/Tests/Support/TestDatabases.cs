using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Tests.Support;

public static class TestDatabases
{
    public const string Unreachable =
        "Host=127.0.0.1;Port=54399;Database=unreachable;Username=postgres;Password=unreachable;Timeout=2;Command Timeout=2";

    public static async Task<string> CreateEmptyAsync(string server)
    {
        string name = $"startup_{Guid.NewGuid():N}";
        NpgsqlConnectionStringBuilder admin = new(server) { Database = "postgres" };

        await using NpgsqlConnection connection = new(admin.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand create = new($"CREATE DATABASE \"{name}\"", connection);
        await create.ExecuteNonQueryAsync();

        return new NpgsqlConnectionStringBuilder(server) { Database = name }.ConnectionString;
    }

    public static async Task<NpgsqlConnection> OpenAsync(string database)
    {
        NpgsqlConnection connection = new(DatabaseSearchPath.Apply(database));
        await connection.OpenAsync();
        return connection;
    }

    public static string WithWrongPassword(string server) =>
        new NpgsqlConnectionStringBuilder(server) { Password = "wrong-password" }.ConnectionString;

    public static void SkipInitialization(IServiceCollection services)
    {
        ServiceDescriptor initialization = services.Single(descriptor =>
            descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == typeof(DatabaseInitialization));
        services.Remove(initialization);
    }
}
