using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Dune.Analyzers.Tests;

public sealed class BusNameAnalyzerTests
{
    private const string Messaging = """
        namespace Stub.DependencyInjection
        {
            public interface IServices { }
        }
        namespace TemplateApp.Infrastructure.Messaging
        {
            using Stub.DependencyInjection;
            public static class MessagingRegistration
            {
                public static IServices AddIntegrationEventPublication<TEvent>(this IServices services, string name, int version, string exchange) => services;
                public static IServices AddIntegrationEventHandler<TEvent, THandler>(this IServices services, string name, int version, string exchange, string queue) => services;
                public static IServices AddRetiredBinding(this IServices services, string exchange, string routingKey, string queue) => services;
                public static IServices AddMessaging(this IServices services, string queue) => services;
            }
        }
        namespace TemplateApp.Infrastructure.Messaging.Outbox
        {
            using Stub.DependencyInjection;
            public static class OutboxRegistration
            {
                public static IServices AddOutboxEvent<TEvent>(this IServices services, string name, int version) => services;
            }
        }
        """;

    private const string TaskEvents = """
        namespace TemplateApp.Features.Tasks.MessageNames;
        internal static class TaskEvents
        {
            public const string Exchange = "tasks.task-events";
            public const string Created = "tasks.task-created";
            public const int Version = 1;
        }
        """;

    private const string UserEvents = """
        namespace TemplateApp.Features.Tasks.MessageNames;
        internal static class UserEvents
        {
            public const string Exchange = "users.user-events";
            public const string AccountClosureRequested = "users.account-closure-requested";
        }
        """;

    private const string OwnerTombstones = """
        namespace TemplateApp.Features.Tasks;
        internal static class OwnerTombstones
        {
            public const string Consumer = "tasks.user-deletions";
        }
        """;

    private const string Events = """
        namespace TemplateApp.Features.Tasks;
        internal sealed record TaskCreated;
        internal sealed record TaskCreatedIntegrationEvent;
        internal sealed class TaskEventsConsumer { }
        """;

    [Fact]
    public async Task EveryBusRegistration_TakesItsNamesFromMessageNames()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Tasks/TasksFeature.cs", """
                using Stub.DependencyInjection;
                using TemplateApp.Features.Tasks.MessageNames;
                using TemplateApp.Infrastructure.Messaging;
                using TemplateApp.Infrastructure.Messaging.Outbox;
                namespace TemplateApp.Features.Tasks;
                internal static class TasksFeature
                {
                    public static IServices AddTasksFeature(this IServices services)
                    {
                        services.AddOutboxEvent<TaskCreated>(TaskEvents.Created, TaskEvents.Version);
                        services.AddIntegrationEventPublication<TaskCreatedIntegrationEvent>(TaskEvents.Created, TaskEvents.Version, TaskEvents.Exchange);
                        services.AddIntegrationEventHandler<TaskCreatedIntegrationEvent, TaskEventsConsumer>(TaskEvents.Created, TaskEvents.Version, TaskEvents.Exchange, OwnerTombstones.Consumer);
                        services.AddRetiredBinding(UserEvents.Exchange, UserEvents.AccountClosureRequested, OwnerTombstones.Consumer);
                        return services.AddMessaging("tasks.anything");
                    }
                }
                """));

        Assert.Empty(Locations(diagnostics));
    }

    [Fact]
    public async Task ALiteral_OrAConstantOutsideTheFeaturesMessageNames_IsRejected()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Tasks/LegacyEvents.cs", """
                namespace TemplateApp.Features.Tasks;
                internal static class LegacyEvents
                {
                    public const string Deleted = "tasks.task-deleted";
                }
                """),
            ("backend/Features/Users/MessageNames/UserEvents.cs", """
                namespace TemplateApp.Features.Users.MessageNames;
                internal static class UserEvents
                {
                    public const string Exchange = "users.user-events";
                }
                """),
            ("backend/Features/Tasks/TasksFeature.cs", """
                using Stub.DependencyInjection;
                using TemplateApp.Features.Tasks.MessageNames;
                using TemplateApp.Infrastructure.Messaging;
                using TemplateApp.Infrastructure.Messaging.Outbox;
                namespace TemplateApp.Features.Tasks;
                internal static class TasksFeature
                {
                    private const int LocalVersion = 1;
                    public static IServices AddTasksFeature(this IServices services)
                    {
                        services.AddOutboxEvent<TaskCreated>("tasks.task-created", LocalVersion);
                        services.AddIntegrationEventPublication<TaskCreatedIntegrationEvent>(LegacyEvents.Deleted, 1, TemplateApp.Features.Users.MessageNames.UserEvents.Exchange);
                        return services.AddRetiredBinding(exchange: TaskEvents.Exchange, routingKey: TaskEvents.Created + "-v2", queue: "tasks.anything");
                    }
                }
                """));

        Assert.Equal(
            new[]
            {
                ("backend/Features/Tasks/TasksFeature.cs", 11),
                ("backend/Features/Tasks/TasksFeature.cs", 11),
                ("backend/Features/Tasks/TasksFeature.cs", 12),
                ("backend/Features/Tasks/TasksFeature.cs", 12),
                ("backend/Features/Tasks/TasksFeature.cs", 12),
                ("backend/Features/Tasks/TasksFeature.cs", 13),
            },
            Locations(diagnostics));
    }

    [Fact]
    public async Task MessageNames_HoldsOnlyStaticClassesOfConstants()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Tasks/MessageNames/TaskCommands.cs", """
                namespace TemplateApp.Features.Tasks.MessageNames;
                internal static class TaskCommands
                {
                    public const string Exchange = "tasks.commands";
                    public static readonly string HoldOwnerTasks = "tasks.hold-owner-tasks";
                }
                """),
            ("backend/Features/Tasks/MessageNames/HoldOwnerTasksIntegrationCommand.cs", """
                namespace TemplateApp.Features.Tasks.MessageNames;
                internal sealed record HoldOwnerTasksIntegrationCommand(string OwnerId);
                """),
            ("backend/Features/Tasks/MessageNames/QueueNames.cs", """
                namespace TemplateApp.Features.Tasks.MessageNames;
                internal sealed class QueueNames
                {
                    public const string Closures = "tasks.account-closures";
                }
                """),
            ("backend/Features/Tasks/MessageNames/MessageNamesEndpoint.cs", """
                namespace TemplateApp.Features.Tasks.MessageNames;
                internal static class MessageNamesEndpoint
                {
                    public static string Send() => TaskEvents.Created;
                }
                """),
            ("backend/Features/Tasks/TasksFeature.cs", """
                using Stub.DependencyInjection;
                using TemplateApp.Features.Tasks.MessageNames;
                using TemplateApp.Infrastructure.Messaging.Outbox;
                namespace TemplateApp.Features.Tasks;
                internal static class TasksFeature
                {
                    public static IServices AddTasksFeature(this IServices services) =>
                        services.AddOutboxEvent<TaskCreated>(TaskEvents.Created, TaskEvents.Version);
                }
                """));

        Assert.Equal(
            new[]
            {
                ("backend/Features/Tasks/MessageNames/HoldOwnerTasksIntegrationCommand.cs", 2),
                ("backend/Features/Tasks/MessageNames/MessageNamesEndpoint.cs", 2),
                ("backend/Features/Tasks/MessageNames/QueueNames.cs", 2),
                ("backend/Features/Tasks/MessageNames/TaskCommands.cs", 2),
            },
            Locations(diagnostics));
    }

    [Fact]
    public async Task WithMessageNamesAndNoBusRegistration_TheRuleSaysItWentBlind()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Tasks/TasksFeature.cs", """
                using Stub.DependencyInjection;
                using TemplateApp.Features.Tasks.MessageNames;
                using TemplateApp.Infrastructure.Bus;
                namespace TemplateApp.Features.Tasks;
                internal static class TasksFeature
                {
                    public static IServices AddTasksFeature(this IServices services) =>
                        services.AddMovedOutboxEvent<TaskCreated>(TaskEvents.Created, TaskEvents.Version);
                }
                """),
            ("backend/Infrastructure/Bus/MovedRegistration.cs", """
                using Stub.DependencyInjection;
                namespace TemplateApp.Infrastructure.Bus;
                public static class MovedRegistration
                {
                    public static IServices AddMovedOutboxEvent<TEvent>(this IServices services, string name, int version) => services;
                }
                """));

        Assert.Equal(new[] { (string.Empty, 0) }, Locations(diagnostics));
    }

    [Fact]
    public async Task TestsMayNameTheBusWithLiterals()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Tests/Messaging/RetiredBindingsTests.cs", """
                using Stub.DependencyInjection;
                using TemplateApp.Infrastructure.Messaging;
                namespace TemplateApp.Tests.Messaging;
                internal static class RetiredBindingsTests
                {
                    public static IServices Arrange(IServices services) =>
                        services.AddRetiredBinding("tasks.task-events", "tasks.task-created", "tests.queue");
                }
                """),
            ("backend/Features/Tasks/TasksFeature.cs", """
                using Stub.DependencyInjection;
                using TemplateApp.Features.Tasks.MessageNames;
                using TemplateApp.Infrastructure.Messaging.Outbox;
                namespace TemplateApp.Features.Tasks;
                internal static class TasksFeature
                {
                    public static IServices AddTasksFeature(this IServices services) =>
                        services.AddOutboxEvent<TaskCreated>(TaskEvents.Created, TaskEvents.Version);
                }
                """));

        Assert.Empty(Locations(diagnostics));
    }

    private static Task<System.Collections.Immutable.ImmutableArray<Diagnostic>> AnalyzeAsync(
        params (string Path, string Source)[] files) =>
        AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>(files)
            {
                ("packages/Messaging.cs", Messaging),
                ("backend/Features/Tasks/MessageNames/TaskEvents.cs", TaskEvents),
                ("backend/Features/Tasks/MessageNames/UserEvents.cs", UserEvents),
                ("backend/Features/Tasks/OwnerTombstones.cs", OwnerTombstones),
                ("backend/Features/Tasks/Events.cs", Events),
            },
            AnalyzerHost.Only<BusNameAnalyzer>());

    private static (string Path, int Line)[] Locations(System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics
            .Where(diagnostic => diagnostic.Id == "DUNE0030")
            .Select(diagnostic => diagnostic.Location.SourceTree is { } tree
                ? (tree.FilePath, diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1)
                : (string.Empty, 0))
            .OrderBy(location => location.Item1, System.StringComparer.Ordinal)
            .ThenBy(location => location.Item2)
            .ToArray();
}
