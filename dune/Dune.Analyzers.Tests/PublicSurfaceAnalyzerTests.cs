using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Dune.Analyzers.Tests;

public sealed class PublicSurfaceAnalyzerTests
{
    [Fact]
    public async Task PublicContractsAndInternalInteriorAreAccepted()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Users/Contracts/IUsersModule.cs", """
                namespace TemplateApp.Features.Users.Contracts;
                public interface IUsersModule
                {
                    UserProfileDetails? FindProfile(string authId);
                }
                """),
            ("backend/Features/Users/Contracts/UserProfileDetails.cs", """
                namespace TemplateApp.Features.Users.Contracts;
                public sealed record UserProfileDetails(string DisplayName);
                """),
            ("backend/Features/Users/Contracts/UserPermissions.cs", """
                namespace TemplateApp.Features.Users.Contracts;
                public static class UserPermissions
                {
                    public const string Read = "users.read";
                }
                """),
            ("backend/Features/Users/UsersModule.cs", """
                using TemplateApp.Features.Users.Contracts;
                namespace TemplateApp.Features.Users;
                internal sealed class UsersModule : IUsersModule
                {
                    public UserProfileDetails? FindProfile(string authId) => null;
                }
                """),
            ("backend/Features/Users/UsersFeature.cs", """
                namespace TemplateApp.Features.Users;
                internal static class UsersFeature
                {
                    public static int Count() => 1;
                }
                """),
            ("backend/Features/Users/Domain/UserProfile.cs", """
                namespace TemplateApp.Features.Users.Domain;
                internal sealed class UserProfile
                {
                    public string DisplayName { get; set; } = "";
                }
                """),
            ("backend/Features/Users/GetUser/GetUser.cs", """
                namespace TemplateApp.Features.Users.GetUser;
                internal sealed class GetUser
                {
                    public sealed record Row(string Name);
                }
                """),
            ("backend/Features/Users/GetUser/GetUserTests.cs", """
                namespace TemplateApp.Features.Users.GetUser;
                public sealed class GetUserTests { }
                """),
            ("backend/Infrastructure/Persistence/AppDbContext.cs", """
                namespace TemplateApp.Infrastructure.Persistence;
                public class AppDbContext { }
                """));

        Assert.Empty(Dune0020(diagnostics));
    }

    [Fact]
    public async Task PublicTypeOutsideContractsIsRejected()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Tasks/CreateTask/CreateTask.cs", """
                namespace TemplateApp.Features.Tasks.CreateTask;
                public sealed class CreateTask { }
                """),
            ("backend/Features/Tasks/CreateTask/CreateTaskEndpoint.cs", """
                namespace TemplateApp.Features.Tasks.CreateTask;
                public static class CreateTaskEndpoint { }
                """),
            ("backend/Features/Tasks/Domain/TaskItem.cs", """
                namespace TemplateApp.Features.Tasks.Domain;
                public sealed class TaskItem { }
                """),
            ("backend/Features/Tasks/TaskResponse.cs", """
                namespace TemplateApp.Features.Tasks;
                public sealed record TaskResponse(string Title);
                """),
            ("backend/Features/Tasks/TasksFeature.cs", """
                namespace TemplateApp.Features.Tasks;
                internal static class TasksFeature { }
                """));

        Assert.Equal(
            new[]
            {
                "CreateTask.cs:2 'CreateTask' es public fuera de Contracts/: el interior de una feature es internal",
                "CreateTaskEndpoint.cs:2 'CreateTaskEndpoint' es public fuera de Contracts/: el interior de una feature es internal",
                "TaskItem.cs:2 'TaskItem' es public fuera de Contracts/: el interior de una feature es internal",
                "TaskResponse.cs:2 'TaskResponse' es public fuera de Contracts/: el interior de una feature es internal",
            },
            Dune0020(diagnostics));
    }

    [Fact]
    public async Task ContractThatIsNotPublicIsRejected()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Users/Contracts/UserProfileDetails.cs", """
                namespace TemplateApp.Features.Users.Contracts;
                internal sealed record UserProfileDetails(string DisplayName);
                """));

        Assert.Equal(
            new[] { "UserProfileDetails.cs:2 'UserProfileDetails' está en Contracts/ y no es public: lo que ve otra feature es public" },
            Dune0020(diagnostics));
    }

    [Fact]
    public async Task PublicTypeInContractsSubfolderIsRejected()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Users/Contracts/Inner/Secret.cs", """
                namespace TemplateApp.Features.Users.Contracts.Inner;
                public sealed class Secret { }
                """));

        Assert.Equal(
            new[] { "Secret.cs:2 'Secret' es public fuera de Contracts/: el interior de una feature es internal" },
            Dune0020(diagnostics));
    }

    [Fact]
    public async Task EveryKindOfPublicTypeOutsideContractsIsRejected()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Tasks/TaskState.cs", """
                namespace TemplateApp.Features.Tasks;
                public enum TaskState { Open }
                """),
            ("backend/Features/Tasks/ITaskClock.cs", """
                namespace TemplateApp.Features.Tasks;
                public interface ITaskClock { }
                """),
            ("backend/Features/Tasks/TaskFilter.cs", """
                namespace TemplateApp.Features.Tasks;
                public delegate bool TaskFilter(string title);
                """),
            ("backend/Features/Tasks/TaskDraft.cs", """
                namespace TemplateApp.Features.Tasks;
                public readonly record struct TaskDraft(string Title);
                """));

        Assert.Equal(
            new[]
            {
                "ITaskClock.cs:2 'ITaskClock' es public fuera de Contracts/: el interior de una feature es internal",
                "TaskDraft.cs:2 'TaskDraft' es public fuera de Contracts/: el interior de una feature es internal",
                "TaskFilter.cs:2 'TaskFilter' es public fuera de Contracts/: el interior de una feature es internal",
                "TaskState.cs:2 'TaskState' es public fuera de Contracts/: el interior de una feature es internal",
            },
            Dune0020(diagnostics));
    }

    [Fact]
    public async Task ContractsNamespaceOutsideContractsFolderIsRejected()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Tasks/CreateTask/TaskDraftDetails.cs", """
                namespace TemplateApp.Features.Tasks.Contracts;
                public readonly record struct TaskDraftDetails(string Title);
                """),
            ("backend/Features/Tasks/CreateTask/TaskFilter.cs", """
                namespace TemplateApp.Features.Tasks.Contracts;
                public delegate bool TaskFilter(string title);
                """));

        Assert.Equal(
            new[]
            {
                "TaskDraftDetails.cs:2 'TaskDraftDetails' es public fuera de Contracts/: el interior de una feature es internal",
                "TaskFilter.cs:2 'TaskFilter' es public fuera de Contracts/: el interior de una feature es internal",
            },
            Dune0020(diagnostics));
    }

    [Fact]
    public async Task ASagaAndTheTypesOfItsPublicSignature_ArePublic_AndNothingElseOfItsSlice()
    {
        var diagnostics = await AnalyzeAsync(
            ("packages/Wolverine.cs", """
                namespace Wolverine;
                public abstract class Saga { }
                """),
            ("backend/Features/Users/OrchestrateAccountClosure/AccountClosureSaga.cs", """
                using System.Threading.Tasks;
                namespace TemplateApp.Features.Users.OrchestrateAccountClosure;
                public sealed class AccountClosureSaga : Wolverine.Saga
                {
                    public AccountClosureState State { get; set; }
                    public Task<ReleaseOwnerTasksIntegrationCommand?> Handle(OwnerTasksReleasedIntegrationEvent released, IAccountClosureSteps steps) => Task.FromResult<ReleaseOwnerTasksIntegrationCommand?>(null);
                    internal static HoldOwnerTasksIntegrationCommand Open() => new();
                }
                """),
            ("backend/Features/Users/OrchestrateAccountClosure/AccountClosureState.cs", """
                namespace TemplateApp.Features.Users.OrchestrateAccountClosure;
                public enum AccountClosureState { HoldingTasks }
                """),
            ("backend/Features/Users/OrchestrateAccountClosure/OwnerTasksReleasedIntegrationEvent.cs", """
                namespace TemplateApp.Features.Users.OrchestrateAccountClosure;
                public sealed record OwnerTasksReleasedIntegrationEvent(System.Guid ClosureId);
                """),
            ("backend/Features/Users/OrchestrateAccountClosure/ReleaseOwnerTasksIntegrationCommand.cs", """
                namespace TemplateApp.Features.Users.OrchestrateAccountClosure;
                public sealed record ReleaseOwnerTasksIntegrationCommand(System.Guid ClosureId);
                """),
            ("backend/Features/Users/OrchestrateAccountClosure/IAccountClosureSteps.cs", """
                namespace TemplateApp.Features.Users.OrchestrateAccountClosure;
                public interface IAccountClosureSteps { }
                """),
            ("backend/Features/Users/OrchestrateAccountClosure/HoldOwnerTasksIntegrationCommand.cs", """
                namespace TemplateApp.Features.Users.OrchestrateAccountClosure;
                public sealed record HoldOwnerTasksIntegrationCommand;
                """),
            ("backend/Features/Users/OrchestrateAccountClosure/AccountClosureSteps.cs", """
                namespace TemplateApp.Features.Users.OrchestrateAccountClosure;
                public sealed class AccountClosureSteps : IAccountClosureSteps { }
                """));

        Assert.Equal(
            new[]
            {
                "AccountClosureSteps.cs:2 'AccountClosureSteps' es public fuera de Contracts/: el interior de una feature es internal",
                "HoldOwnerTasksIntegrationCommand.cs:2 'HoldOwnerTasksIntegrationCommand' es public fuera de Contracts/: el interior de una feature es internal",
            },
            Dune0020(diagnostics));
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(params (string Path, string Source)[] files) =>
        AnalyzerHost.AnalyzeAsync(files, AnalyzerHost.Only<PublicSurfaceAnalyzer>());

    private static string[] Dune0020(ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics
            .Where(diagnostic => diagnostic.Id == "DUNE0020")
            .Select(diagnostic =>
            {
                var span = diagnostic.Location.GetLineSpan();
                return $"{System.IO.Path.GetFileName(span.Path)}:{span.StartLinePosition.Line + 1} {diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}";
            })
            .OrderBy(location => location, System.StringComparer.Ordinal)
            .ToArray();
}
