using TemplateApp.Features.Users.Contracts;
using TemplateApp.Features.Users.Domain.Events;
using TemplateApp.Features.Users.Persistence;
using TemplateApp.Infrastructure.Http;
using TemplateApp.Infrastructure.Messaging;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Users;

internal static class UsersFeature
{
    public const string TasksService = "Tasks";

    public static IServiceCollection AddUsersFeature(this IServiceCollection services)
    {
        services.AddMigratedDbContext<UsersDbContext>(UsersDbContext.Schema, adoptsLegacyTables: true);
        services.AddOutbox<UsersDbContext>();
        services.AddOutboxEvent<UserDeleted>(UserEvents.Deleted, UserEvents.Version);
        services.AddIntegrationEventPublication<PublishUserEvents.UserDeletedIntegrationEvent>(UserEvents.Deleted, UserEvents.Version, UserEvents.Exchange);
        services.AddEventHandler<UserDeleted, PublishUserEvents.UserEventPublisher>();
        services.AddScoped<IUsersModule, UsersModule>();
        services.AddRemoteClient<OwnedTasksClient>(TasksService);
        services.AddScoped<GetUser.GetUser>();
        services.AddScoped<UpdateProfile.UpdateProfile>();
        services.AddScoped<DeleteUser.DeleteUser>();
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
