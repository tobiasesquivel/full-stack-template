using Microsoft.AspNetCore.Authentication.Cookies;
using TemplateApp.Features.Tasks.Access;
using TemplateApp.Features.Tasks.Persistence;
using TemplateApp.Infrastructure.Http;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Tasks;

internal static class TasksFeature
{
    public const string MonolithService = "Monolith";

    public static IServiceCollection AddTasksFeature(this IServiceCollection services)
    {
        services.AddMigratedDbContext<TasksDbContext>(TasksDbContext.Schema, adoptsLegacyTables: true);
        services.AddSessionCookie();
        services.AddOptions<CookieAuthenticationOptions>(SessionCookie.Scheme)
            .Configure(cookie => cookie.Events.OnValidatePrincipal = RemoteSession.ValidatePrincipalAsync);
        services.AddRemoteClient<AuthSessionClient>(MonolithService);
        services.AddRemoteClient<ProjectsClient>(MonolithService);

        services.AddSingleton<TaskMetrics>();
        services.AddScoped<TaskAccess>();
        services.AddScoped<CreateTask.CreateTask>();
        services.AddScoped<ListTasks.ListTasks>();
        services.AddScoped<GetTask.GetTask>();
        services.AddScoped<UpdateTask.UpdateTask>();
        services.AddScoped<DeleteTask.DeleteTask>();
        services.AddScoped<CountTasksByProject.CountTasksByProject>();
        services.AddScoped<DeleteOwnedTasks.DeleteOwnedTasks>();
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

    public static RouteGroupBuilder MapTasksInternalEndpoints(this RouteGroupBuilder internalApi)
    {
        CountTasksByProject.CountTasksByProjectEndpoint.Map(internalApi);
        DeleteOwnedTasks.DeleteOwnedTasksEndpoint.Map(internalApi);

        return internalApi;
    }
}
