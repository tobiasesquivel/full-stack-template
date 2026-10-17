namespace TemplateApp.Features.Tasks;

public static class TasksFeature
{
    public static IServiceCollection AddTasksFeature(this IServiceCollection services)
    {
        services.AddScoped<TasksModule>();
        return services;
    }

    public static IEndpointRouteBuilder MapTasksEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tasks").RequireAuthorization();

        group.MapPost("", TasksEndpoints.CreateAsync);
        group.MapGet("", TasksEndpoints.ListAsync);
        group.MapGet("/{id:guid}", TasksEndpoints.GetByIdAsync);
        group.MapPut("/{id:guid}", TasksEndpoints.UpdateAsync);
        group.MapDelete("/{id:guid}", TasksEndpoints.DeleteAsync);

        return app;
    }
}
