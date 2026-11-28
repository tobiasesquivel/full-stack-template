using Npgsql;
using TemplateApp.Infrastructure.Messaging.Mediator;
using TemplateApp.Infrastructure.Persistence;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;

namespace TemplateApp.Infrastructure.Messaging.Sagas;

internal static class SagaStorage
{
    public const string Schema = "wolverine";

    public static void Configure(WolverineOptions options)
    {
        options.Services.AddKeyedSingleton(Schema, (provider, _) =>
            NpgsqlDataSource.Create(DatabaseSearchPath.ConnectionString(provider)));
        options.PersistMessagesWithPostgresqlUsingDataSourceFromContainer(provider => provider.GetRequiredKeyedService<NpgsqlDataSource>(Schema), Schema);
        options.UseEntityFrameworkCoreTransactions();
        options.Policies.Add(new MediatorTransactionPolicy());
        options.Policies.Add(new DurableLocalQueuesPolicy());
    }
}
