using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Dune.Analyzers.Tests;

public sealed class EventHandlerCallAnalyzerTests
{
    private const string Platform = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        namespace TemplateApp.Shared.Domain
        {
            public interface IDomainEvent { }
        }
        namespace TemplateApp.Infrastructure.Messaging
        {
            public sealed record EventContext(Guid MessageId, string Consumer);
        }
        namespace TemplateApp.Infrastructure.Messaging.Outbox
        {
            using TemplateApp.Shared.Domain;

            public interface IEventHandler<in TEvent>
                where TEvent : class, IDomainEvent
            {
                Task HandleAsync(TEvent @event, EventContext context, CancellationToken ct);
            }
        }
        namespace TemplateApp.Features.Tasks.Domain.Events
        {
            using TemplateApp.Shared.Domain;

            public sealed record TaskCreated(Guid TaskId) : IDomainEvent;
        }
        namespace TemplateApp.Features.Tasks
        {
            using TemplateApp.Features.Tasks.Domain.Events;
            using TemplateApp.Infrastructure.Messaging;
            using TemplateApp.Infrastructure.Messaging.Outbox;

            public sealed class TaskCounter : IEventHandler<TaskCreated>
            {
                public Task HandleAsync(TaskCreated @event, EventContext context, CancellationToken ct) => Task.CompletedTask;
            }
        }
        """;

    [Fact]
    public async Task OnlyTheRelayCallsAConsumer()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Infrastructure/Messaging/Outbox/OutboxRelay.cs", """
                using System;
                using System.Threading;
                using System.Threading.Tasks;
                using TemplateApp.Infrastructure.Messaging;
                using TemplateApp.Shared.Domain;
                namespace TemplateApp.Infrastructure.Messaging.Outbox;
                public sealed class OutboxRelay
                {
                    public static Task DispatchAsync<TEvent, THandler>(Func<THandler> resolve, IDomainEvent @event, EventContext context, CancellationToken ct)
                        where TEvent : class, IDomainEvent
                        where THandler : class, IEventHandler<TEvent> =>
                        resolve().HandleAsync((TEvent)@event, context, ct);
                }
                """),
            ("backend/Features/Tasks/TasksFeature.cs", """
                using System;
                using TemplateApp.Features.Tasks.Domain.Events;
                using TemplateApp.Infrastructure.Messaging.Outbox;
                namespace TemplateApp.Features.Tasks;
                public static class TasksFeature
                {
                    public static Type Registered() => typeof(IEventHandler<TaskCreated>);

                    public static string Consumer() => typeof(TaskCounter).FullName!;
                }
                """),
            ("backend/Tests/Messaging/TaskCounterTests.cs", """
                using System;
                using System.Threading.Tasks;
                using TemplateApp.Features.Tasks;
                using TemplateApp.Features.Tasks.Domain.Events;
                using TemplateApp.Infrastructure.Messaging;
                namespace TemplateApp.Tests.Messaging;
                public sealed class TaskCounterTests
                {
                    public Task CountsAsync() =>
                        new TaskCounter().HandleAsync(new TaskCreated(Guid.NewGuid()), new EventContext(Guid.NewGuid(), "test"), default);
                }
                """));

        Assert.Empty(Locations(diagnostics));
    }

    [Fact]
    public async Task ACallOutsideTheRelayIsRejected()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Tasks/CreateTask/CreateTask.cs", """
                using System;
                using System.Threading;
                using System.Threading.Tasks;
                using TemplateApp.Features.Tasks.Domain.Events;
                using TemplateApp.Infrastructure.Messaging;
                using TemplateApp.Infrastructure.Messaging.Outbox;
                namespace TemplateApp.Features.Tasks.CreateTask;
                public sealed class CreateTask(IEventHandler<TaskCreated> consumer)
                {
                    public async Task ExecuteAsync(CancellationToken ct)
                    {
                        var created = new TaskCreated(Guid.NewGuid());
                        await Task.Delay(1, ct);
                        await consumer.HandleAsync(created, new EventContext(Guid.NewGuid(), "slice"), ct);
                    }
                }
                """),
            ("backend/Features/Tasks/DeleteTask/DeleteTask.cs", """
                using System;
                using System.Threading;
                using System.Threading.Tasks;
                using TemplateApp.Features.Tasks.Domain.Events;
                namespace TemplateApp.Features.Tasks.DeleteTask;
                public sealed class DeleteTask
                {
                    public Task ExecuteAsync(CancellationToken ct)
                    {
                        var counter = new TaskCounter();
                        return counter.HandleAsync(new TaskCreated(Guid.NewGuid()), null!, ct);
                    }
                }
                """),
            ("backend/Infrastructure/Messaging/Outbox/OutboxInterceptor.cs", """
                using System;
                using System.Threading;
                using System.Threading.Tasks;
                using TemplateApp.Infrastructure.Messaging;
                using TemplateApp.Shared.Domain;
                namespace TemplateApp.Infrastructure.Messaging.Outbox;
                public sealed class OutboxInterceptor
                {
                    public static Task PublishAsync<TEvent, THandler>(THandler handler, TEvent @event, CancellationToken ct)
                        where TEvent : class, IDomainEvent
                        where THandler : IEventHandler<TEvent> =>
                        handler.HandleAsync(@event, new EventContext(Guid.NewGuid(), "interceptor"), ct);
                }
                """));

        Assert.Equal(
            new[]
            {
                ("backend/Features/Tasks/CreateTask/CreateTask.cs", 14),
                ("backend/Features/Tasks/DeleteTask/DeleteTask.cs", 10),
                ("backend/Features/Tasks/DeleteTask/DeleteTask.cs", 11),
                ("backend/Infrastructure/Messaging/Outbox/OutboxInterceptor.cs", 12),
            },
            Locations(diagnostics));
    }

    private static Task<System.Collections.Immutable.ImmutableArray<Diagnostic>> AnalyzeAsync(
        params (string Path, string Source)[] files) =>
        AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>(files) { ("backend/Platform.cs", Platform) },
            AnalyzerHost.Only<EventHandlerCallAnalyzer>());

    private static (string Path, int Line)[] Locations(System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics
            .Where(diagnostic => diagnostic.Id == "DUNE0023")
            .Select(diagnostic => (
                diagnostic.Location.SourceTree!.FilePath,
                diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1))
            .OrderBy(location => location.FilePath, System.StringComparer.Ordinal)
            .ThenBy(location => location.Item2)
            .ToArray();
}
