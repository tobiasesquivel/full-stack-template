namespace TemplateApp.Features.Users;

public static class UsersFeature
{
    public static IServiceCollection AddUsersFeature(this IServiceCollection services)
    {
        services.AddScoped<UsersModule>();
        return services;
    }

    public static IEndpointRouteBuilder MapUsersEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/users").RequireAuthorization();

        group.MapGet("/me", UsersEndpoints.GetMeAsync);
        group.MapPut("/me", UsersEndpoints.UpdateMeAsync);
        group.MapDelete("/me", UsersEndpoints.DeleteMeAsync);
        group.MapGet("/{authId}", UsersEndpoints.GetByIdAsync);
        group.MapDelete("/{authId}", UsersEndpoints.DeleteByIdAsync);

        return app;
    }
}
