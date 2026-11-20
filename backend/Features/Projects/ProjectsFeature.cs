using TemplateApp.Features.Projects.Contracts;
using TemplateApp.Features.Projects.Persistence;
using TemplateApp.Infrastructure.Http;
using TemplateApp.Infrastructure.Messaging;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Projects;

internal static class ProjectsFeature
{
    public const string TasksService = "Tasks";

    public static IServiceCollection AddProjectsFeature(this IServiceCollection services)
    {
        services.AddMigratedDbContext<ProjectsDbContext>(ProjectsDbContext.Schema, adoptsLegacyTables: false);
        services.AddScoped<IProjectsModule, ProjectsModule>();
        services.AddScoped<ProjectResponses>();
        services.AddScoped<FindAssignableProject.FindAssignableProject>();
        services.AddScoped<CreateProject.CreateProject>();
        services.AddScoped<ListProjects.ListProjects>();
        services.AddScoped<GetProject.GetProject>();
        services.AddScoped<UpdateProject.UpdateProject>();
        services.AddScoped<ApplyTaskEvent.ApplyTaskEvent>();
        services.AddIntegrationEventHandler<ApplyTaskEvent.TaskCreatedIntegrationEvent, ApplyTaskEvent.TaskEventsConsumer>(TaskEvents.Created, TaskEvents.Version, TaskEvents.Exchange, TaskCountersProjection.Consumer);
        services.AddIntegrationEventHandler<ApplyTaskEvent.TaskCompletedIntegrationEvent, ApplyTaskEvent.TaskEventsConsumer>(TaskEvents.Completed, TaskEvents.Version, TaskEvents.Exchange, TaskCountersProjection.Consumer);
        services.AddIntegrationEventHandler<ApplyTaskEvent.TaskReopenedIntegrationEvent, ApplyTaskEvent.TaskEventsConsumer>(TaskEvents.Reopened, TaskEvents.Version, TaskEvents.Exchange, TaskCountersProjection.Consumer);
        services.AddIntegrationEventHandler<ApplyTaskEvent.TaskDeletedIntegrationEvent, ApplyTaskEvent.TaskEventsConsumer>(TaskEvents.Deleted, TaskEvents.Version, TaskEvents.Exchange, TaskCountersProjection.Consumer);
        services.AddRemoteClient<RebuildTaskCounters.TaskCountsSnapshotClient>(TasksService, RemoteRetry.Idempotent);
        services.AddScoped<RebuildTaskCounters.RebuildTaskCounters>();
        services.AddHostedService<RebuildTaskCounters.RebuildTaskCountersOnStartup>();
        return services;
    }

    public static RouteGroupBuilder MapProjectsEndpoints(this RouteGroupBuilder api)
    {
        RouteGroupBuilder projects = api.MapGroup("/projects").RequireAuthorization();

        CreateProject.CreateProjectEndpoint.Map(projects);
        ListProjects.ListProjectsEndpoint.Map(projects);
        GetProject.GetProjectEndpoint.Map(projects);
        UpdateProject.UpdateProjectEndpoint.Map(projects);

        return api;
    }

    public static RouteGroupBuilder MapProjectsInternalEndpoints(this RouteGroupBuilder internalApi)
    {
        FindAssignableProject.FindAssignableProjectEndpoint.Map(internalApi);

        return internalApi;
    }
}
