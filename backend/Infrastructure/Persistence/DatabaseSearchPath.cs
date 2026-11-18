using Npgsql;

namespace TemplateApp.Infrastructure.Persistence;

public static class DatabaseSearchPath
{
    public const string Schemas = "public,auth,users,tasks,platform,projects";

    public static string Apply(string connectionString) =>
        new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = Schemas }.ConnectionString;
}
