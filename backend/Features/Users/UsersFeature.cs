using FluentValidation;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Features.Users.Domain.Events;
using TemplateApp.Features.Users.OrchestrateAccountClosure;
using TemplateApp.Features.Users.Persistence;
using TemplateApp.Infrastructure.Messaging;
using TemplateApp.Infrastructure.Messaging.Mediator;
using TemplateApp.Infrastructure.Messaging.Outbox;
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
        services.AddIntegrationEventPublication<PublishUserEvents.UserDeletedIntegrationEvent>(UserEvents.Deleted, UserEvents.Version, UserEvents.Exchange);
        services.AddEventHandler<UserDeleted, PublishUserEvents.UserEventPublisher>();
        services.AddSaga<AccountClosureSaga, UsersDbContext>();
        services.AddSaga<ScheduleAccountClosure.AccountClosureGracePeriod, UsersDbContext>();
        services.AddIntegrationCommand<HoldOwnerTasksIntegrationCommand>(TaskCommands.HoldOwnerTasks, TaskCommands.Version, TaskCommands.Exchange);
        services.AddIntegrationCommand<ReleaseOwnerTasksIntegrationCommand>(TaskCommands.ReleaseOwnerTasks, TaskCommands.Version, TaskCommands.Exchange);
        services.AddIntegrationCommand<DecideAccountClosureInfo>(AuthCommands.DecideAccountClosure, AuthCommands.Version, AuthCommands.Exchange);
        services.AddSagaMessage<OwnerTasksHeldIntegrationEvent>(TaskEvents.OwnerTasksHeld, TaskEvents.Version, TaskEvents.Exchange, ClosuresConsumer);
        services.AddSagaMessage<OwnerTasksReleasedIntegrationEvent>(TaskEvents.OwnerTasksReleased, TaskEvents.Version, TaskEvents.Exchange, ClosuresConsumer);
        services.AddSagaMessage<AccountDeletedInfo>(AuthEvents.AccountDeleted, AuthEvents.Version, AuthEvents.Exchange, ClosuresConsumer);
        services.AddSagaMessage<AccountClosureRejectedInfo>(AuthEvents.AccountClosureRejected, AuthEvents.Version, AuthEvents.Exchange, ClosuresConsumer);
        services.AddSingleton<UserMetrics>();
        services.AddScoped<IUsersModule, UsersModule>();
        services.AddMediatorHandler<GetUser.GetUser.Handler>(MediatorTransaction.None);
        services.AddMediatorHandler<GetAccountClosure.GetAccountClosure.Handler>(MediatorTransaction.None);
        services.AddMediatorHandler<GetAccountClosureHistory.GetAccountClosureHistory.Handler>(MediatorTransaction.None);
        services.AddMediatorHandler<WithdrawAccountClosure.WithdrawAccountClosure.Handler>(MediatorTransaction.Atomic);
        services.AddMediatorHandler<UpdateProfile.UpdateProfile.Handler>(MediatorTransaction.Atomic);
        services.AddSingleton<IValidator<UpdateProfile.UpdateProfileCommand>, UpdateProfile.UpdateProfileCommandValidator>();
        services.AddMediatorHandler<DeleteUser.DeleteUser.Handler>(MediatorTransaction.Eager);
        services.AddScoped<CompleteAccountClosure.CompleteAccountClosure>();
        services.AddScoped<CancelAccountClosure.CancelAccountClosure>();
        services.AddAfterCommit<UsersDbContext>();
        services.AddScoped<IAccountClosureSteps, AccountClosureSteps>();
        services.AddScoped<AccountClosureHistory>();
        services.AddScoped<StartAccountClosure.StartAccountClosure>();
        services.AddScoped<RemindAccountClosure.RemindAccountClosure>();
        services.AddScoped<ScheduleAccountClosure.IAccountClosureGraceSteps, ScheduleAccountClosure.AccountClosureGraceSteps>();
        return services;
    }

    public static RouteGroupBuilder MapUsersEndpoints(this RouteGroupBuilder api)
    {
        RouteGroupBuilder users = api.MapGroup("/users").RequireAuthorization();

        GetUser.GetUserEndpoint.Map(users);
        GetAccountClosure.GetAccountClosureEndpoint.Map(users);
        GetAccountClosureHistory.GetAccountClosureHistoryEndpoint.Map(users);
        WithdrawAccountClosure.WithdrawAccountClosureEndpoint.Map(users);
        UpdateProfile.UpdateProfileEndpoint.Map(users);
        DeleteUser.DeleteUserEndpoint.Map(users);

        return api;
    }
}
