namespace TemplateApp.Features.Tasks;

public static class TasksFeature
{
    public static IServiceCollection AddTasksFeature(this IServiceCollection services)
    {
        services.AddScoped<CreateTask.CreateTask>();
        services.AddScoped<ListTasks.ListTasks>();
        services.AddScoped<GetTask.GetTask>();
        services.AddScoped<UpdateTask.UpdateTask>();
        services.AddScoped<DeleteTask.DeleteTask>();
        return services;
    }

    public static IEndpointRouteBuilder MapTasksEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder tasks = app.MapGroup("/api/v1/tasks").RequireAuthorization();

        CreateTask.CreateTaskEndpoint.Map(tasks);
        ListTasks.ListTasksEndpoint.Map(tasks);
        GetTask.GetTaskEndpoint.Map(tasks);
        UpdateTask.UpdateTaskEndpoint.Map(tasks);
        DeleteTask.DeleteTaskEndpoint.Map(tasks);

        return app;
    }
}
