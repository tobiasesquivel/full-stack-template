using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Features.Users.Domain.Events;
using TemplateApp.Features.Users.OrchestrateAccountClosure;
using TemplateApp.Features.Users.Persistence;
using TemplateApp.Infrastructure.Messaging;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Users;

internal static class UsersFeature
{
    public const string ClosuresConsumer = "users.account-closures";

    public static IServiceCollection AddUsersFeature(this IServiceCollection services)
    {
        services.AddMigratedDbContext<UsersDbContext>(UsersDbContext.Schema, adoptsLegacyTables: true);
        services.AddOptionsWithValidateOnStart<AccountClosureOptions, AccountClosureOptionsValidator>()
            .BindConfiguration(AccountClosureOptions.Section);
        services.AddOutbox<UsersDbContext>();
        services.AddOutboxEvent<UserDeleted>(UserEvents.Deleted, UserEvents.Version);
        services.AddOutboxEvent<AccountClosureRequested>(UserEvents.AccountClosureRequested, UserEvents.Version);
        services.AddIntegrationEventPublication<PublishUserEvents.UserDeletedIntegrationEvent>(UserEvents.Deleted, UserEvents.Version, UserEvents.Exchange);
        services.AddIntegrationEventPublication<PublishUserEvents.AccountClosureRequestedIntegrationEvent>(UserEvents.AccountClosureRequested, UserEvents.Version, UserEvents.Exchange);
        services.AddEventHandler<UserDeleted, PublishUserEvents.UserEventPublisher>();
        services.AddEventHandler<AccountClosureRequested, PublishUserEvents.UserEventPublisher>();
        services.AddSaga<AccountClosureSaga, UsersDbContext>();
        services.AddIntegrationCommand<HoldOwnerTasksIntegrationCommand>(TaskCommands.HoldOwnerTasks, TaskCommands.Version, TaskCommands.Exchange);
        services.AddIntegrationCommand<ReleaseOwnerTasksIntegrationCommand>(TaskCommands.ReleaseOwnerTasks, TaskCommands.Version, TaskCommands.Exchange);
        services.AddIntegrationCommand<DecideAccountClosureInfo>(AuthCommands.DecideAccountClosure, AuthCommands.Version, AuthCommands.Exchange);
        services.AddSagaMessage<OwnerTasksHeldIntegrationEvent>(TaskEvents.OwnerTasksHeld, TaskEvents.Version, TaskEvents.Exchange, ClosuresConsumer);
        services.AddSagaMessage<OwnerTasksReleasedIntegrationEvent>(TaskEvents.OwnerTasksReleased, TaskEvents.Version, TaskEvents.Exchange, ClosuresConsumer);
        services.AddSagaMessage<AccountDeletedInfo>(AuthEvents.AccountDeleted, AuthEvents.Version, AuthEvents.Exchange, ClosuresConsumer);
        services.AddSagaMessage<AccountClosureRejectedInfo>(AuthEvents.AccountClosureRejected, AuthEvents.Version, AuthEvents.Exchange, ClosuresConsumer);
        services.AddSingleton<UserMetrics>();
        services.AddScoped<IUsersModule, UsersModule>();
        services.AddScoped<GetUser.GetUser>();
        services.AddScoped<GetAccountClosure.GetAccountClosure>();
        services.AddScoped<UpdateProfile.UpdateProfile>();
        services.AddScoped<DeleteUser.DeleteUser>();
        services.AddScoped<CompleteAccountClosure.CompleteAccountClosure>();
        services.AddScoped<CancelAccountClosure.CancelAccountClosure>();
        services.AddScoped<IAccountClosureSteps, AccountClosureSteps>();
        return services;
    }

    public static RouteGroupBuilder MapUsersEndpoints(this RouteGroupBuilder api)
    {
        RouteGroupBuilder users = api.MapGroup("/users").RequireAuthorization();

        GetUser.GetUserEndpoint.Map(users);
        GetAccountClosure.GetAccountClosureEndpoint.Map(users);
        UpdateProfile.UpdateProfileEndpoint.Map(users);
        DeleteUser.DeleteUserEndpoint.Map(users);

        return api;
    }
}
