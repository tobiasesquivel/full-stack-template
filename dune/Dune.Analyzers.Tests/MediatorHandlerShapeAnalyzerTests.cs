using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Dune.Analyzers.Tests;

public sealed class MediatorHandlerShapeAnalyzerTests
{
    private const string CreateTaskCommand = """
        namespace TemplateApp.Features.Tasks.CreateTask;
        internal sealed partial record CreateTaskCommand(string Title);
        """;

    private const string GetTaskQuery = """
        namespace TemplateApp.Features.Tasks.GetTask;
        internal sealed partial record GetTaskQuery(int Id);
        """;

    [Fact]
    public async Task CommandAndQueryHandlers_InTheirOwnFiles_AreAccepted()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Tasks/CreateTask/CreateTaskCommand.cs", CreateTaskCommand),
            ("backend/Features/Tasks/CreateTask/CreateTaskCommandHandler.cs", """
                namespace TemplateApp.Features.Tasks.CreateTask;
                internal sealed partial record CreateTaskCommand
                {
                    public sealed class Handler
                    {
                        public int Handle(CreateTaskCommand command) => command.Title.Length;
                    }
                }
                """),
            ("backend/Features/Tasks/GetTask/GetTaskQuery.cs", GetTaskQuery),
            ("backend/Features/Tasks/GetTask/GetTaskQueryHandler.cs", """
                namespace TemplateApp.Features.Tasks.GetTask;
                internal sealed partial record GetTaskQuery
                {
                    public sealed class Handler
                    {
                        public int Handle(GetTaskQuery query) => query.Id;
                    }
                }
                """));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task HandlerNestedInAStaticClassNamedAfterTheSlice_IsReported()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Tasks/CreateTask/CreateTaskCommand.cs", """
                namespace TemplateApp.Features.Tasks.CreateTask;
                internal sealed record CreateTaskCommand(string Title);
                """),
            ("backend/Features/Tasks/CreateTask/CreateTask.cs", """
                namespace TemplateApp.Features.Tasks.CreateTask;
                internal static class CreateTask
                {
                    public sealed class Handler
                    {
                        public int Handle(CreateTaskCommand command) => command.Title.Length;
                    }
                }
                """));

        Assert.Contains("'CreateTask', que no es un record", Assert.Single(diagnostics).GetMessage());
    }

    [Fact]
    public async Task HandlerInsideARecordThatIsNeitherCommandNorQuery_IsReported()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Projects/GetProject/GetProjectRequestHandler.cs", """
                namespace TemplateApp.Features.Projects.GetProject;
                internal sealed record GetProjectRequest(int Id)
                {
                    public sealed class Handler
                    {
                        public int Handle(GetProjectRequest request) => request.Id;
                    }
                }
                """));

        Assert.Contains("'GetProjectRequest', que no es un record", Assert.Single(diagnostics).GetMessage());
    }

    [Fact]
    public async Task HandlerWithAnotherName_IsReported()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Users/GetUser/GetUserQuery.cs", """
                namespace TemplateApp.Features.Users.GetUser;
                internal sealed partial record GetUserQuery(string Id);
                """),
            ("backend/Features/Users/GetUser/GetUserQueryHandler.cs", """
                namespace TemplateApp.Features.Users.GetUser;
                internal sealed partial record GetUserQuery
                {
                    public sealed class GetUserQueryHandler
                    {
                        public string Handle(GetUserQuery query) => query.Id;
                    }
                }
                """));

        Assert.Contains("se llama 'GetUserQueryHandler' y no 'Handler'", Assert.Single(diagnostics).GetMessage());
    }

    [Fact]
    public async Task HandlerDeclaredInTheMessageFile_IsReported()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Auth/RegisterUser/RegisterUserCommand.cs", """
                namespace TemplateApp.Features.Auth.RegisterUser;
                internal sealed record RegisterUserCommand(string UserName)
                {
                    public sealed class Handler
                    {
                        public int Handle(RegisterUserCommand command) => command.UserName.Length;
                    }
                }
                """));

        Assert.Contains("vive en 'RegisterUserCommand.cs' y no en 'RegisterUserCommandHandler.cs'", Assert.Single(diagnostics).GetMessage());
    }

    [Fact]
    public async Task MessageDeclaredOnlyInTheHandlerFile_IsReported()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Tasks/CreateTask/CreateTaskCommandHandler.cs", """
                namespace TemplateApp.Features.Tasks.CreateTask;
                internal sealed record CreateTaskCommand(string Title)
                {
                    public sealed class Handler
                    {
                        public int Handle(CreateTaskCommand command) => command.Title.Length;
                    }
                }
                """));

        Assert.Contains("no se declara en 'CreateTaskCommand.cs'", Assert.Single(diagnostics).GetMessage());
    }

    [Fact]
    public async Task TestsAndFeaturesWithoutTheMediator_AreLeftOut()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Tasks/CreateTask/CreateTaskHelpersTests.cs", """
                namespace TemplateApp.Features.Tasks.CreateTask;
                internal static class CreateTaskHelpersTests
                {
                    public sealed class Fake
                    {
                        public int Handle(int value) => value;
                    }
                }
                """),
            ("backend/Features/Health/GetLiveness/GetLiveness.cs", """
                namespace TemplateApp.Features.Health.GetLiveness;
                internal static class GetLiveness
                {
                    public sealed class Handler
                    {
                        public int Handle(int value) => value;
                    }
                }
                """));

        Assert.Empty(diagnostics);
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(params (string Path, string Source)[] files) =>
        AnalyzerHost.AnalyzeAsync(new List<(string, string)>(files), AnalyzerHost.Only<MediatorHandlerShapeAnalyzer>());
}
