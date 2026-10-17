using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Dune.Analyzers.Tests;

public sealed class OneTypePerFileAnalyzerTests
{
    [Fact]
    public async Task OneTypeWithFileAndNestedHelpersIsAccepted()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Tests/Support/TestSession.cs", """
                    namespace TemplateApp.Tests.Support;
                    internal sealed class TestSession
                    {
                        public string Read() => new CookieParser().Parse() + new Pending().Name;
                        private sealed class Pending { public string Name => "pending"; }
                    }
                    file sealed class CookieParser { public string Parse() => "cookie"; }
                    """),
            },
            AnalyzerHost.Only<OneTypePerFileAnalyzer>());

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DUNE0010");
    }

    [Fact]
    public async Task SecondTopLevelTypeIsRejected()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Tests/Support/TestSession.cs", """
                    namespace TemplateApp.Tests.Support;
                    internal sealed record SessionResponse(int Status);
                    internal sealed class TestSession { }
                    internal interface ISessionClock { }
                    """),
                ("backend/Features/Tasks/TaskErrors.cs", """
                    namespace TemplateApp.Features.Tasks
                    {
                        public static class TaskErrors { }
                        public enum TaskErrorKind { Missing }
                    }
                    """),
            },
            AnalyzerHost.Only<OneTypePerFileAnalyzer>());

        var rejected = diagnostics
            .Where(diagnostic => diagnostic.Id == "DUNE0010")
            .Select(diagnostic => $"{System.IO.Path.GetFileName(diagnostic.Location.SourceTree!.FilePath)}:{diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1}")
            .OrderBy(entry => entry, System.StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[] { "TaskErrors.cs:4", "TestSession.cs:2", "TestSession.cs:4" }, rejected);
    }

    [Fact]
    public async Task MigrationsAreExempt()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Infrastructure/Persistence/Migrations/20260101000000_Initial.cs", """
                    namespace TemplateApp.Infrastructure.Persistence.Migrations;
                    public partial class Initial { }
                    public partial class InitialExtra { }
                    """),
            },
            AnalyzerHost.Only<OneTypePerFileAnalyzer>());

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DUNE0010");
    }
}
