using TemplateApp.Features.Tasks.Contracts;
using TemplateApp.Features.Tasks.Persistence;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Tasks;

internal static class TasksFeature
{
    public static IServiceCollection AddTasksFeature(this IServiceCollection services)
    {
        services.AddMigratedDbContext<TasksDbContext>(TasksDbContext.Schema, adoptsLegacyTables: true);
        services.AddSingleton<TaskMetrics>();
        services.AddScoped<ITasksModule, TasksModule>();
        services.AddScoped<CreateTask.CreateTask>();
        services.AddScoped<ListTasks.ListTasks>();
        services.AddScoped<GetTask.GetTask>();
        services.AddScoped<UpdateTask.UpdateTask>();
        services.AddScoped<DeleteTask.DeleteTask>();
        return services;
    }

    public static RouteGroupBuilder MapTasksEndpoints(this RouteGroupBuilder api)
    {
        RouteGroupBuilder tasks = api.MapGroup("/tasks").RequireAuthorization();

        CreateTask.CreateTaskEndpoint.Map(tasks);
        ListTasks.ListTasksEndpoint.Map(tasks);
        GetTask.GetTaskEndpoint.Map(tasks);
        UpdateTask.UpdateTaskEndpoint.Map(tasks);
        DeleteTask.DeleteTaskEndpoint.Map(tasks);

        return api;
    }
}
