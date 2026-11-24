using FluentValidation;
using TemplateApp.Features.Projects.Contracts;
using TemplateApp.Features.Projects.Persistence;
using TemplateApp.Infrastructure.Http.Remote;
using TemplateApp.Infrastructure.Messaging;
using TemplateApp.Infrastructure.Messaging.Mediator;
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
        services.AddMediatorHandler<FindAssignableProject.FindAssignableProject.Handler>(MediatorTransaction.None);
        services.AddMediatorHandler<CreateProject.CreateProject.Handler>(MediatorTransaction.Atomic);
        services.AddSingleton<IValidator<CreateProject.CreateProjectCommand>, CreateProject.CreateProjectCommandValidator>();
        services.AddMediatorHandler<ListProjects.ListProjects.Handler>(MediatorTransaction.None);
        services.AddMediatorHandler<GetProject.GetProject.Handler>(MediatorTransaction.None);
        services.AddMediatorHandler<UpdateProject.UpdateProject.Handler>(MediatorTransaction.Atomic);
        services.AddSingleton<IValidator<UpdateProject.UpdateProjectCommand>, UpdateProject.UpdateProjectCommandValidator>();
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
