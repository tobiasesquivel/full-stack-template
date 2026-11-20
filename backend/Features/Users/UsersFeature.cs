using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Features.Users.Domain.Events;
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
        services.AddOutbox<UsersDbContext>();
        services.AddOutboxEvent<UserDeleted>(UserEvents.Deleted, UserEvents.Version);
        services.AddOutboxEvent<AccountClosureRequested>(UserEvents.AccountClosureRequested, UserEvents.Version);
        services.AddIntegrationEventPublication<PublishUserEvents.UserDeletedIntegrationEvent>(UserEvents.Deleted, UserEvents.Version, UserEvents.Exchange);
        services.AddIntegrationEventPublication<PublishUserEvents.AccountClosureRequestedIntegrationEvent>(UserEvents.AccountClosureRequested, UserEvents.Version, UserEvents.Exchange);
        services.AddEventHandler<UserDeleted, PublishUserEvents.UserEventPublisher>();
        services.AddEventHandler<AccountClosureRequested, PublishUserEvents.UserEventPublisher>();
        services.AddIntegrationEventHandler<AccountDeletedInfo, CompleteAccountClosure.AccountDeletionsConsumer>(AuthEvents.AccountDeleted, AuthEvents.Version, AuthEvents.Exchange, ClosuresConsumer);
        services.AddIntegrationEventHandler<AccountClosureRejectedInfo, CancelAccountClosure.AccountClosureRejectionsConsumer>(AuthEvents.AccountClosureRejected, AuthEvents.Version, AuthEvents.Exchange, ClosuresConsumer);
        services.AddSingleton<UserMetrics>();
        services.AddScoped<IUsersModule, UsersModule>();
        services.AddScoped<GetUser.GetUser>();
        services.AddScoped<UpdateProfile.UpdateProfile>();
        services.AddScoped<DeleteUser.DeleteUser>();
        services.AddScoped<CompleteAccountClosure.CompleteAccountClosure>();
        services.AddScoped<CancelAccountClosure.CancelAccountClosure>();
        return services;
    }

    public static RouteGroupBuilder MapUsersEndpoints(this RouteGroupBuilder api)
    {
        RouteGroupBuilder users = api.MapGroup("/users").RequireAuthorization();

        GetUser.GetUserEndpoint.Map(users);
        UpdateProfile.UpdateProfileEndpoint.Map(users);
        DeleteUser.DeleteUserEndpoint.Map(users);

        return api;
    }
}
