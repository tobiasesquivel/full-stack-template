using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Dune.Analyzers.Tests;

public sealed class MediatorEndpointAnalyzerTests
{
    private const string Wolverine = """
        using System.Threading;
        using System.Threading.Tasks;
        namespace Wolverine
        {
            public interface IMessageBus
            {
                Task InvokeAsync(object message, CancellationToken ct = default);
                Task<T> InvokeAsync<T>(object message, CancellationToken ct = default);
            }
        }
        """;

    private const string CreateTaskSlice = """
        namespace TemplateApp.Features.Tasks.CreateTask;
        internal sealed record CreateTaskCommand(string Title);
        """;

    private const string CreateTaskHandler = """
        namespace TemplateApp.Features.Tasks.CreateTask;
        internal static class CreateTask
        {
            public sealed class Handler
            {
                public int Handle(CreateTaskCommand command) => command.Title.Length;
            }
        }
        """;

    private const string CreateProjectSlice = """
        namespace TemplateApp.Features.Projects.CreateProject;
        internal sealed record CreateProjectCommand(string Name);
        """;

    private const string CreateProjectHandler = """
        namespace TemplateApp.Features.Projects.CreateProject;
        internal static class CreateProject
        {
            public sealed class Handler
            {
                public int Handle(CreateProjectCommand command) => command.Name.Length;
            }
        }
        """;

    private const string UpdateTaskSlice = """
        namespace TemplateApp.Features.Tasks.UpdateTask;
        internal sealed record UpdateTaskCommand(string Title);
        """;

    [Fact]
    public async Task TheEndpoint_DispatchesTheCommandOfItsSlice_Once()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Tasks/CreateTask/CreateTaskEndpoint.cs", """
                using System.Threading;
                using System.Threading.Tasks;
                using Wolverine;
                namespace TemplateApp.Features.Tasks.CreateTask;
                internal static class CreateTaskEndpoint
                {
                    public static Task<int> HandleAsync(IMessageBus bus, CancellationToken ct) =>
                        bus.InvokeAsync<int>(new CreateTaskCommand("Pan"), ct);
                }
                """),
            ("backend/Features/Projects/CreateProject/CreateProjectEndpoint.cs", """
                using System.Threading;
                using System.Threading.Tasks;
                using Wolverine;
                namespace TemplateApp.Features.Projects.CreateProject;
                internal static class CreateProjectEndpoint
                {
                    public static Task<int> HandleAsync(IMessageBus bus, CancellationToken ct) =>
                        bus.InvokeAsync<int>(new CreateProjectCommand("Mudanza"), ct);
                }
                """),
            ("backend/Features/Health/GetLiveness/GetLivenessEndpoint.cs", """
                namespace TemplateApp.Features.Health.GetLiveness;
                internal static class GetLivenessEndpoint
                {
                    public static int HandleAsync() => 1;
                }
                """));

        Assert.Empty(Locations(diagnostics));
    }

    [Fact]
    public async Task TheEndpoint_ThatCallsTheHandler_OrDispatchesAnotherSlice_OrTwice_IsRejected()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Tasks/CreateTask/CreateTaskEndpoint.cs", """
                using System.Threading;
                using System.Threading.Tasks;
                using Wolverine;
                namespace TemplateApp.Features.Tasks.CreateTask;
                internal static class CreateTaskEndpoint
                {
                    public static int Direct(CreateTask.Handler handler) => handler.Handle(new CreateTaskCommand("Pan"));
                    public static int Built() => new CreateTask.Handler().Handle(new CreateTaskCommand("Pan"));
                    public static Task Untyped(IMessageBus bus) => bus.InvokeAsync(new CreateTaskCommand("Pan"));
                }
                """),
            ("backend/Features/Tasks/UpdateTask/UpdateTaskEndpoint.cs", """
                using System.Threading;
                using System.Threading.Tasks;
                using Wolverine;
                namespace TemplateApp.Features.Tasks.UpdateTask;
                internal static class UpdateTaskEndpoint
                {
                    public static Task<int> Foreign(IMessageBus bus, CancellationToken ct) =>
                        bus.InvokeAsync<int>(new TemplateApp.Features.Tasks.CreateTask.CreateTaskCommand("Pan"), ct);
                    public static Task<int> Again(IMessageBus bus, CancellationToken ct) =>
                        bus.InvokeAsync<int>(new UpdateTaskCommand("Pan"), ct);
                }
                """),
            ("backend/Features/Projects/CreateProject/CreateProjectEndpoint.cs", """
                namespace TemplateApp.Features.Projects.CreateProject;
                internal static class CreateProjectEndpoint
                {
                    public static int Direct(CreateProject.Handler handler) => handler.Handle(new CreateProjectCommand("Mudanza"));
                }
                """));

        Assert.Equal(
            new[]
            {
                ("backend/Features/Projects/CreateProject/CreateProjectEndpoint.cs", 2),
                ("backend/Features/Projects/CreateProject/CreateProjectEndpoint.cs", 4),
                ("backend/Features/Tasks/CreateTask/CreateTaskEndpoint.cs", 5),
                ("backend/Features/Tasks/CreateTask/CreateTaskEndpoint.cs", 7),
                ("backend/Features/Tasks/CreateTask/CreateTaskEndpoint.cs", 8),
                ("backend/Features/Tasks/UpdateTask/UpdateTaskEndpoint.cs", 8),
                ("backend/Features/Tasks/UpdateTask/UpdateTaskEndpoint.cs", 10),
            },
            Locations(diagnostics));
    }

    private static Task<System.Collections.Immutable.ImmutableArray<Diagnostic>> AnalyzeAsync(
        params (string Path, string Source)[] files) =>
        AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>(files)
            {
                ("packages/Wolverine.cs", Wolverine),
                ("backend/Features/Tasks/CreateTask/CreateTaskCommand.cs", CreateTaskSlice),
                ("backend/Features/Tasks/CreateTask/CreateTask.cs", CreateTaskHandler),
                ("backend/Features/Tasks/UpdateTask/UpdateTaskCommand.cs", UpdateTaskSlice),
                ("backend/Features/Projects/CreateProject/CreateProjectCommand.cs", CreateProjectSlice),
                ("backend/Features/Projects/CreateProject/CreateProject.cs", CreateProjectHandler),
            },
            AnalyzerHost.Only<MediatorEndpointAnalyzer>());

    private static (string Path, int Line)[] Locations(System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics
            .Where(diagnostic => diagnostic.Id == "DUNE0028")
            .Select(diagnostic => (
                diagnostic.Location.SourceTree!.FilePath,
                diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1))
            .OrderBy(location => location.FilePath, System.StringComparer.Ordinal)
            .ThenBy(location => location.Item2)
            .ToArray();
}
