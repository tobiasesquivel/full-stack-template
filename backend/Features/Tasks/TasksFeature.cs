using FluentValidation;
using Microsoft.AspNetCore.Authentication.Cookies;
using TemplateApp.Features.Tasks.Access;
using TemplateApp.Features.Tasks.Domain.Events;
using TemplateApp.Features.Tasks.MessageNames;
using TemplateApp.Features.Tasks.Persistence;
using TemplateApp.Infrastructure.Http;
using TemplateApp.Infrastructure.Http.Remote;
using TemplateApp.Infrastructure.Messaging;
using TemplateApp.Infrastructure.Messaging.Mediator;
using TemplateApp.Infrastructure.Messaging.Outbox;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Tasks;

internal static class TasksFeature
{
    public const string MonolithService = "Monolith";

    public static IServiceCollection AddTasksFeature(this IServiceCollection services)
    {
        services.AddMigratedDbContext<TasksDbContext>(TasksDbContext.Schema, adoptsLegacyTables: true);
        services.AddOutbox<TasksDbContext>();
        services.AddOutboxEvent<TaskCreated>(TaskEvents.Created, TaskEvents.Version);
        services.AddOutboxEvent<TaskCompleted>(TaskEvents.Completed, TaskEvents.Version);
        services.AddOutboxEvent<TaskReopened>(TaskEvents.Reopened, TaskEvents.Version);
        services.AddOutboxEvent<TaskDeleted>(TaskEvents.Deleted, TaskEvents.Version);
        services.AddOutboxEvent<OwnerTasksHeld>(TaskEvents.OwnerTasksHeld, TaskEvents.Version);
        services.AddOutboxEvent<OwnerTasksReleased>(TaskEvents.OwnerTasksReleased, TaskEvents.Version);
        services.AddSessionCookie();
        services.AddOptions<CookieAuthenticationOptions>(SessionCookie.Scheme)
            .Configure(cookie => cookie.Events.OnValidatePrincipal = RemoteSession.ValidatePrincipalAsync);
        services.AddRemoteClient<AuthSessionClient>(MonolithService, RemoteRetry.Idempotent);
        services.AddRemoteClient<ProjectsClient>(MonolithService, RemoteRetry.Idempotent);
        services.AddIntegrationEventPublication<PublishTaskEvents.TaskCreatedIntegrationEvent>(TaskEvents.Created, TaskEvents.Version, TaskEvents.Exchange);
        services.AddIntegrationEventPublication<PublishTaskEvents.TaskCompletedIntegrationEvent>(TaskEvents.Completed, TaskEvents.Version, TaskEvents.Exchange);
        services.AddIntegrationEventPublication<PublishTaskEvents.TaskReopenedIntegrationEvent>(TaskEvents.Reopened, TaskEvents.Version, TaskEvents.Exchange);
        services.AddIntegrationEventPublication<PublishTaskEvents.TaskDeletedIntegrationEvent>(TaskEvents.Deleted, TaskEvents.Version, TaskEvents.Exchange);
        services.AddIntegrationEventPublication<PublishTaskEvents.OwnerTasksHeldIntegrationEvent>(TaskEvents.OwnerTasksHeld, TaskEvents.Version, TaskEvents.Exchange);
        services.AddIntegrationEventPublication<PublishTaskEvents.OwnerTasksReleasedIntegrationEvent>(TaskEvents.OwnerTasksReleased, TaskEvents.Version, TaskEvents.Exchange);
        services.AddEventHandler<TaskCreated, PublishTaskEvents.TaskEventPublisher>();
        services.AddEventHandler<TaskCompleted, PublishTaskEvents.TaskEventPublisher>();
        services.AddEventHandler<TaskReopened, PublishTaskEvents.TaskEventPublisher>();
        services.AddEventHandler<TaskDeleted, PublishTaskEvents.TaskEventPublisher>();
        services.AddEventHandler<OwnerTasksHeld, PublishTaskEvents.TaskEventPublisher>();
        services.AddEventHandler<OwnerTasksReleased, PublishTaskEvents.TaskEventPublisher>();
        services.AddIntegrationEventHandler<DeleteOwnedTasks.UserDeletedIntegrationEvent, DeleteOwnedTasks.UserDeletionsConsumer>(UserEvents.Deleted, UserEvents.Version, UserEvents.Exchange, OwnerTombstones.Consumer);
        services.AddRetiredBinding(UserEvents.Exchange, UserEvents.AccountClosureRequested, OwnerTombstones.ClosuresConsumer);
        services.AddRetiredBinding(AuthEvents.Exchange, AuthEvents.AccountClosureRejected, OwnerTombstones.ClosuresConsumer);
        services.AddIntegrationEventHandler<HoldOwnerTasks.HoldOwnerTasksIntegrationCommand, HoldOwnerTasks.HoldOwnerTasksCommandsConsumer>(TaskCommands.HoldOwnerTasks, TaskCommands.Version, TaskCommands.Exchange, OwnerTombstones.ClosuresConsumer);
        services.AddIntegrationEventHandler<ReleaseOwnerTasks.ReleaseOwnerTasksIntegrationCommand, ReleaseOwnerTasks.ReleaseOwnerTasksCommandsConsumer>(TaskCommands.ReleaseOwnerTasks, TaskCommands.Version, TaskCommands.Exchange, OwnerTombstones.ClosuresConsumer);

        services.AddSingleton<TaskMetrics>();
        services.AddScoped<TaskAccess>();
        services.AddMediatorHandler<CreateTask.CreateTask.Handler>(MediatorTransaction.None);
        services.AddSingleton<IValidator<CreateTask.CreateTaskCommand>, CreateTask.CreateTaskCommandValidator>();
        services.AddMediatorHandler<ListTasks.ListTasks.Handler>(MediatorTransaction.None);
        services.AddMediatorHandler<GetTask.GetTask.Handler>(MediatorTransaction.None);
        services.AddMediatorHandler<UpdateTask.UpdateTask.Handler>(MediatorTransaction.None);
        services.AddSingleton<IValidator<UpdateTask.UpdateTaskCommand>, UpdateTask.UpdateTaskCommandValidator>();
        services.AddMediatorHandler<DeleteTask.DeleteTask.Handler>(MediatorTransaction.None);
        services.AddMediatorHandler<SnapshotTaskCounts.SnapshotTaskCounts.Handler>(MediatorTransaction.None);
        services.AddScoped<DeleteOwnedTasks.DeleteOwnedTasks>();
        services.AddScoped<HoldOwnerTasks.HoldOwnerTasks>();
        services.AddScoped<ReleaseOwnerTasks.ReleaseOwnerTasks>();
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
        SnapshotTaskCounts.SnapshotTaskCountsEndpoint.Map(internalApi);

        return internalApi;
    }
}
