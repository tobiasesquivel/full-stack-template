using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Dune.Analyzers.Tests;

public sealed class BusBoundaryAnalyzerTests
{
    private const string Wolverine = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        namespace Wolverine
        {
            public sealed class Envelope { public Guid Id { get; set; } }
            public sealed class DeliveryOptions { }
            public interface IMessageBus
            {
                Task PublishAsync<T>(T message, DeliveryOptions? options = null);
                Task InvokeAsync(object message, CancellationToken ct = default);
                Task<T> InvokeAsync<T>(object message, CancellationToken ct = default);
                Task<T> InvokeAsync<T>(object message, DeliveryOptions options, CancellationToken ct = default);
                Task<T> InvokeForTenantAsync<T>(string tenantId, object message, CancellationToken ct = default);
            }
            public class MessageContext { public Envelope? Envelope { get; set; } }
            public abstract class Saga { }
            public abstract record TimeoutMessage(TimeSpan DelayTime);
            public static class WolverineOptionsExtensions
            {
                public static object AddWolverine(this object services) => services;
            }
        }
        namespace Wolverine.Runtime.Handlers
        {
            public abstract class MessageHandler<T>
            {
                protected abstract Task HandleAsync(T message, Wolverine.MessageContext context, CancellationToken cancellation);
            }
        }
        namespace TemplateApp.Infrastructure.Messaging
        {
            public interface IIntegrationEventHandler<in TEvent>
            {
                Task HandleAsync(TEvent @event, CancellationToken ct);
            }
        }
        """;

    [Fact]
    public async Task WolverineLivesInInfrastructureMessaging_AndFeaturesUseTheRepoInterfaces()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Infrastructure/Messaging/Bus/IntegrationEventHandler.cs", """
                using System.Threading;
                using System.Threading.Tasks;
                using Wolverine;
                using Wolverine.Runtime.Handlers;
                namespace TemplateApp.Infrastructure.Messaging;
                public sealed class IntegrationEventHandler<T> : MessageHandler<T>
                {
                    protected override Task HandleAsync(T message, MessageContext context, CancellationToken cancellation) =>
                        Task.FromResult(context.Envelope!.Id);
                }
                """),
            ("backend/Infrastructure/Messaging/Bus/IntegrationEventPublisher.cs", """
                using System.Threading.Tasks;
                using Wolverine;
                namespace TemplateApp.Infrastructure.Messaging;
                public sealed class IntegrationEventPublisher(IMessageBus bus)
                {
                    public Task PublishAsync<T>(T @event) => bus.PublishAsync(@event, new DeliveryOptions());
                }
                """),
            ("backend/Features/Projects/ApplyTaskEvent/TaskEventsConsumer.cs", """
                using System.Threading;
                using System.Threading.Tasks;
                using TemplateApp.Infrastructure.Messaging;
                namespace TemplateApp.Features.Projects.ApplyTaskEvent;
                public sealed record TaskCreatedIntegrationEvent(System.Guid TaskId);
                public sealed class TaskEventsConsumer : IIntegrationEventHandler<TaskCreatedIntegrationEvent>
                {
                    public Task HandleAsync(TaskCreatedIntegrationEvent @event, CancellationToken ct) => Task.CompletedTask;
                }
                """),
            ("backend/Tests/Messaging/BrokerTests.cs", """
                using System.Threading.Tasks;
                using Wolverine;
                namespace TemplateApp.Tests.Messaging;
                public sealed class BrokerTests
                {
                    public Task PublishAsync(IMessageBus bus) => bus.InvokeAsync(new object());
                }
                """));

        Assert.Empty(Locations(diagnostics));
    }

    [Fact]
    public async Task WolverineInAFeature_OrAsAMediator_IsRejected()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Projects/ApplyTaskEvent/TaskEventsHandler.cs", """
                using System;
                using Wolverine;
                namespace TemplateApp.Features.Projects.ApplyTaskEvent;
                public sealed class TaskEventsHandler
                {
                    public Guid Handle(Envelope envelope) => envelope.Id;
                }
                """),
            ("backend/Features/Tasks/CreateTask/CreateTaskEndpoint.cs", """
                using System.Threading.Tasks;
                namespace TemplateApp.Features.Tasks.CreateTask;
                public static class CreateTaskEndpoint
                {
                    public static Task HandleAsync(global::Wolverine.IMessageBus bus) => bus.InvokeAsync(new object());
                }
                """),
            ("backend/Infrastructure/Observability/BusRegistration.cs", """
                using Wolverine;
                namespace TemplateApp.Infrastructure.Observability;
                public static class BusRegistration
                {
                    public static object Add(object services) => services.AddWolverine();
                }
                """),
            ("backend/Infrastructure/Messaging/Bus/IntegrationEventPublisher.cs", """
                using System.Threading.Tasks;
                using Wolverine;
                namespace TemplateApp.Infrastructure.Messaging;
                public sealed class IntegrationEventPublisher(IMessageBus bus)
                {
                    public Task<int> AskAsync(object question) => bus.InvokeAsync<int>(question);
                }
                """));

        Assert.Equal(
            new[]
            {
                ("backend/Features/Projects/ApplyTaskEvent/TaskEventsHandler.cs", 2),
                ("backend/Features/Projects/ApplyTaskEvent/TaskEventsHandler.cs", 6),
                ("backend/Features/Projects/ApplyTaskEvent/TaskEventsHandler.cs", 6),
                ("backend/Features/Tasks/CreateTask/CreateTaskEndpoint.cs", 5),
                ("backend/Features/Tasks/CreateTask/CreateTaskEndpoint.cs", 5),
                ("backend/Infrastructure/Messaging/Bus/IntegrationEventPublisher.cs", 6),
                ("backend/Infrastructure/Observability/BusRegistration.cs", 1),
                ("backend/Infrastructure/Observability/BusRegistration.cs", 5),
            },
            Locations(diagnostics));
    }

    [Fact]
    public async Task ASliceEndpoint_DispatchesItsCommandWithTheGenericInvokeAsync()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Tasks/CreateTask/CreateTaskEndpoint.cs", """
                using System.Threading;
                using System.Threading.Tasks;
                using Wolverine;
                namespace TemplateApp.Features.Tasks.CreateTask;
                internal sealed record CreateTaskCommand(string Title);
                internal static class CreateTaskEndpoint
                {
                    public static Task<int> HandleAsync(IMessageBus bus, CancellationToken ct) =>
                        bus.InvokeAsync<int>(new CreateTaskCommand("Pan"), ct);
                }
                """));

        Assert.Empty(Locations(diagnostics));
    }

    [Theory]
    [InlineData("MessageNames")]
    [InlineData("Access")]
    public async Task AnEndpointInATechnicalFolder_IsNotASliceEndpoint(string folder)
    {
        var path = $"backend/Features/Tasks/{folder}/{folder}Endpoint.cs";
        var diagnostics = await AnalyzeAsync(
            (path, $$"""
                using System.Threading;
                using System.Threading.Tasks;
                using Wolverine;
                namespace TemplateApp.Features.Tasks.{{folder}};
                internal sealed record {{folder}}Command(string Title);
                internal static class {{folder}}Endpoint
                {
                    public static Task<int> HandleAsync(IMessageBus bus, CancellationToken ct) =>
                        bus.InvokeAsync<int>(new {{folder}}Command("Pan"), ct);
                }
                """));

        Assert.Equal(
            new[] { (path, 3), (path, 8), (path, 9), (path, 9) },
            Locations(diagnostics));
    }

    [Fact]
    public async Task ASliceEndpoint_NamesOnlyIMessageBus_AndCallsOnlyTheGenericInvokeAsync()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Tasks/UpdateTask/UpdateTaskEndpoint.cs", """
                using System.Threading;
                using System.Threading.Tasks;
                using Wolverine;
                namespace TemplateApp.Features.Tasks.UpdateTask;
                internal static class UpdateTaskEndpoint
                {
                    public static System.Guid IdOf(Envelope envelope) => envelope.Id;
                    public static Task PublishAsync(IMessageBus bus) => bus.PublishAsync(new object());
                    public static Task<int> WithOptionsAsync(IMessageBus bus, CancellationToken ct) => bus.InvokeAsync<int>(new object(), new DeliveryOptions(), ct);
                    public static Task<int> ForTenantAsync(IMessageBus bus, CancellationToken ct) => bus.InvokeForTenantAsync<int>("t", new object(), ct);
                }
                """),
            ("backend/Features/Tasks/UpdateTask/UpdateTask.cs", """
                using System.Threading;
                using System.Threading.Tasks;
                using Wolverine;
                namespace TemplateApp.Features.Tasks.UpdateTask;
                internal static class UpdateTask
                {
                    public static Task<int> HandleAsync(IMessageBus bus, CancellationToken ct) => bus.InvokeAsync<int>(new object(), ct);
                }
                """));

        Assert.Equal(
            new[]
            {
                ("backend/Features/Tasks/UpdateTask/UpdateTask.cs", 3),
                ("backend/Features/Tasks/UpdateTask/UpdateTask.cs", 7),
                ("backend/Features/Tasks/UpdateTask/UpdateTask.cs", 7),
                ("backend/Features/Tasks/UpdateTask/UpdateTask.cs", 7),
                ("backend/Features/Tasks/UpdateTask/UpdateTaskEndpoint.cs", 7),
                ("backend/Features/Tasks/UpdateTask/UpdateTaskEndpoint.cs", 7),
                ("backend/Features/Tasks/UpdateTask/UpdateTaskEndpoint.cs", 8),
                ("backend/Features/Tasks/UpdateTask/UpdateTaskEndpoint.cs", 9),
                ("backend/Features/Tasks/UpdateTask/UpdateTaskEndpoint.cs", 9),
                ("backend/Features/Tasks/UpdateTask/UpdateTaskEndpoint.cs", 9),
                ("backend/Features/Tasks/UpdateTask/UpdateTaskEndpoint.cs", 10),
                ("backend/Features/Tasks/UpdateTask/UpdateTaskEndpoint.cs", 10),
            },
            Locations(diagnostics));
    }

    [Fact]
    public async Task TheOrchestratorFile_NamesWolverine_AndNoOtherFileOfItsSliceDoes()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Users/OrchestrateAccountClosure/AccountClosureSaga.cs", """
                using System;
                using Wolverine;
                namespace TemplateApp.Features.Users.OrchestrateAccountClosure;
                internal sealed class AccountClosureSaga : Saga
                {
                    public Guid Id { get; set; }
                    public Guid Handle(Envelope envelope) => envelope.Id;
                }
                """),
            ("backend/Features/Users/OrchestrateAccountClosure/AccountClosureOverdue.cs", """
                using System;
                using Wolverine;
                namespace TemplateApp.Features.Users.OrchestrateAccountClosure;
                internal sealed record AccountClosureOverdue(Guid ClosureId, TimeSpan After) : TimeoutMessage(After);
                """),
            ("backend/Features/Users/OrchestrateAccountClosure/ClosureEnvelopes.cs", """
                using System;
                using Wolverine;
                namespace TemplateApp.Features.Users.OrchestrateAccountClosure;
                internal static class ClosureEnvelopes
                {
                    public static Guid IdOf(Envelope envelope) => envelope.Id;
                }
                """),
            ("backend/Features/Users/OrchestrateAccountClosure/AskingSaga.cs", """
                using System.Threading.Tasks;
                using Wolverine;
                namespace TemplateApp.Features.Users.OrchestrateAccountClosure;
                internal sealed class AskingSaga : Saga
                {
                    public Task Handle(IMessageBus bus) => bus.InvokeAsync(new object());
                }
                """));

        Assert.Equal(
            new[]
            {
                ("backend/Features/Users/OrchestrateAccountClosure/AskingSaga.cs", 6),
                ("backend/Features/Users/OrchestrateAccountClosure/ClosureEnvelopes.cs", 2),
                ("backend/Features/Users/OrchestrateAccountClosure/ClosureEnvelopes.cs", 6),
                ("backend/Features/Users/OrchestrateAccountClosure/ClosureEnvelopes.cs", 6),
            },
            Locations(diagnostics));
    }

    private static Task<System.Collections.Immutable.ImmutableArray<Diagnostic>> AnalyzeAsync(
        params (string Path, string Source)[] files) =>
        AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>(files) { ("packages/Wolverine.cs", Wolverine) },
            AnalyzerHost.Only<BusBoundaryAnalyzer>());

    private static (string Path, int Line)[] Locations(System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics
            .Where(diagnostic => diagnostic.Id == "DUNE0025")
            .Select(diagnostic => (
                diagnostic.Location.SourceTree!.FilePath,
                diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1))
            .OrderBy(location => location.FilePath, System.StringComparer.Ordinal)
            .ThenBy(location => location.Item2)
            .ToArray();
}
