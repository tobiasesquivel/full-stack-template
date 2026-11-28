using FluentValidation;
using TemplateApp.Features.Projects.Contracts;
using TemplateApp.Features.Projects.MessageNames;
using TemplateApp.Features.Projects.Persistence;
using TemplateApp.Infrastructure.Hosting;
using TemplateApp.Infrastructure.Http.Remote;
using TemplateApp.Infrastructure.Messaging;
using TemplateApp.Infrastructure.Messaging.Mediator;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Projects;

internal sealed class ProjectsFeature : IFeature
{
    public const string TasksService = "Tasks";

    public void AddServices(IServiceCollection services)
    {
        services.AddMigratedDbContext<ProjectsDbContext>(ProjectsDbContext.Schema, adoptsLegacyTables: false);
        services.AddScoped<IProjectsModule, ProjectsModule>();
        services.AddScoped<ProjectResponses>();
        services.AddMediatorHandler<FindAssignableProject.FindAssignableProjectQuery.Handler>(MediatorTransaction.None);
        services.AddMediatorHandler<CreateProject.CreateProjectCommand.Handler>(MediatorTransaction.Atomic);
        services.AddSingleton<IValidator<CreateProject.CreateProjectCommand>, CreateProject.CreateProjectCommandValidator>();
        services.AddMediatorHandler<ListProjects.ListProjectsQuery.Handler>(MediatorTransaction.None);
        services.AddSingleton<IValidator<ListProjects.ListProjectsQuery>, ListProjects.ListProjectsQueryValidator>();
        services.AddMediatorHandler<GetProject.GetProjectQuery.Handler>(MediatorTransaction.None);
        services.AddMediatorHandler<UpdateProject.UpdateProjectCommand.Handler>(MediatorTransaction.Atomic);
        services.AddSingleton<IValidator<UpdateProject.UpdateProjectCommand>, UpdateProject.UpdateProjectCommandValidator>();
        services.AddScoped<ApplyTaskEvent.ApplyTaskEvent>();
        services.AddIntegrationEventHandler<ApplyTaskEvent.TaskCreatedIntegrationEvent, ApplyTaskEvent.TaskEventsConsumer>(TaskEvents.Created, TaskEvents.Version, TaskEvents.Exchange, TaskCountersProjection.Consumer);
        services.AddIntegrationEventHandler<ApplyTaskEvent.TaskCompletedIntegrationEvent, ApplyTaskEvent.TaskEventsConsumer>(TaskEvents.Completed, TaskEvents.Version, TaskEvents.Exchange, TaskCountersProjection.Consumer);
        services.AddIntegrationEventHandler<ApplyTaskEvent.TaskReopenedIntegrationEvent, ApplyTaskEvent.TaskEventsConsumer>(TaskEvents.Reopened, TaskEvents.Version, TaskEvents.Exchange, TaskCountersProjection.Consumer);
        services.AddIntegrationEventHandler<ApplyTaskEvent.TaskDeletedIntegrationEvent, ApplyTaskEvent.TaskEventsConsumer>(TaskEvents.Deleted, TaskEvents.Version, TaskEvents.Exchange, TaskCountersProjection.Consumer);
        services.AddRemoteClient<RebuildTaskCounters.TaskCountsSnapshotClient>(TasksService, RemoteRetry.Idempotent);
        services.AddScoped<RebuildTaskCounters.RebuildTaskCounters>();
        services.AddHostedService<RebuildTaskCounters.RebuildTaskCountersOnStartup>();
    }

    public void MapEndpoints(RouteGroupBuilder api)
    {
        RouteGroupBuilder projects = api.MapGroup("/projects").RequireAuthorization();

        CreateProject.CreateProjectEndpoint.Map(projects);
        ListProjects.ListProjectsEndpoint.Map(projects);
        GetProject.GetProjectEndpoint.Map(projects);
        UpdateProject.UpdateProjectEndpoint.Map(projects);
    }

    public void MapInternalEndpoints(RouteGroupBuilder internalApi)
    {
        FindAssignableProject.FindAssignableProjectEndpoint.Map(internalApi);
    }
}
