using TemplateApp.Features.Users.Contracts;

namespace TemplateApp.Features.Users;

public static class UsersFeature
{
    public static IServiceCollection AddUsersFeature(this IServiceCollection services)
    {
        services.AddScoped<IUserProfiles, UserProfiles>();
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
