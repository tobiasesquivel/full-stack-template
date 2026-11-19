using Microsoft.AspNetCore.Authentication.Cookies;
using TemplateApp.Features.Tasks.Access;
using TemplateApp.Features.Tasks.Domain.Events;
using TemplateApp.Features.Tasks.Persistence;
using TemplateApp.Infrastructure.Http;
using TemplateApp.Infrastructure.Messaging;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Tasks;

internal static class TasksFeature
{
    public const string MonolithService = "Monolith";

    public static IServiceCollection AddTasksFeature(this IServiceCollection services)
    {
        services.AddMigratedDbContext<TasksDbContext>(TasksDbContext.Schema, adoptsLegacyTables: true);
        services.AddOutbox<TasksDbContext>();
        services.AddOutboxEvent<TaskCreated>("tasks.task-created", version: 1);
        services.AddOutboxEvent<TaskCompleted>("tasks.task-completed", version: 1);
        services.AddOutboxEvent<TaskReopened>("tasks.task-reopened", version: 1);
        services.AddOutboxEvent<TaskDeleted>("tasks.task-deleted", version: 1);
        services.AddSessionCookie();
        services.AddOptions<CookieAuthenticationOptions>(SessionCookie.Scheme)
            .Configure(cookie => cookie.Events.OnValidatePrincipal = RemoteSession.ValidatePrincipalAsync);
        services.AddRemoteClient<AuthSessionClient>(MonolithService);
        services.AddRemoteClient<ProjectsClient>(MonolithService);
        services.AddRemoteClient<ForwardTaskEvents.TaskEventsClient>(MonolithService);
        services.AddEventHandler<TaskCreated, ForwardTaskEvents.TaskEventForwarder>();
        services.AddEventHandler<TaskCompleted, ForwardTaskEvents.TaskEventForwarder>();
        services.AddEventHandler<TaskReopened, ForwardTaskEvents.TaskEventForwarder>();
        services.AddEventHandler<TaskDeleted, ForwardTaskEvents.TaskEventForwarder>();

        services.AddSingleton<TaskMetrics>();
        services.AddScoped<TaskAccess>();
        services.AddScoped<CreateTask.CreateTask>();
        services.AddScoped<ListTasks.ListTasks>();
        services.AddScoped<GetTask.GetTask>();
        services.AddScoped<UpdateTask.UpdateTask>();
        services.AddScoped<DeleteTask.DeleteTask>();
        services.AddScoped<DeleteOwnedTasks.DeleteOwnedTasks>();
        services.AddScoped<SnapshotTaskCounts.SnapshotTaskCounts>();
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
        DeleteOwnedTasks.DeleteOwnedTasksEndpoint.Map(internalApi);
        SnapshotTaskCounts.SnapshotTaskCountsEndpoint.Map(internalApi);

        return internalApi;
    }
}
