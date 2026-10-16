using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Dune.Analyzers.Tests;

public sealed class DomainBodyAnalyzerTests
{
    [Fact]
    public async Task DomainRejectsEfUseInsideMethodBody()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Features/Tasks/Domain/TaskRules.cs", """
                    using Microsoft.EntityFrameworkCore;
                    namespace TemplateApp.Features.Tasks.Domain;
                    public sealed class TaskRules
                    {
                        public object Check(DbContext context) => context;
                    }
                    """),
            },
            AnalyzerHost.Only<DomainDependencyAnalyzer>());

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DUNE0001");
    }

    [Fact]
    public async Task DomainRejectsEfUseInsidePropertyBody()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Features/Tasks/Domain/TaskRules.cs", """
                    using Microsoft.EntityFrameworkCore;
                    namespace TemplateApp.Features.Tasks.Domain;
                    public sealed class TaskRules
                    {
                        public DbContext Context => throw new System.InvalidOperationException();
                    }
                    """),
            },
            AnalyzerHost.Only<DomainDependencyAnalyzer>());

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DUNE0001");
    }

    [Fact]
    public async Task DomainRejectsEfUseInsideLambdaBody()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Features/Tasks/Domain/TaskRules.cs", """
                    using System;
                    using Microsoft.EntityFrameworkCore;
                    namespace TemplateApp.Features.Tasks.Domain;
                    public sealed class TaskRules
                    {
                        public Func<object> Check(DbContext context) => () => context;
                    }
                    """),
            },
            AnalyzerHost.Only<DomainDependencyAnalyzer>());

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DUNE0001");
    }

    [Fact]
    public async Task DomainRejectsLocalTypeOfEfContext()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Features/Tasks/Domain/TaskRules.cs", """
                    using System;
                    using Microsoft.EntityFrameworkCore;
                    namespace TemplateApp.Features.Tasks.Domain;
                    public sealed class TaskRules
                    {
                        public Type Check() => typeof(DbContext);
                    }
                    """),
            },
            AnalyzerHost.Only<DomainDependencyAnalyzer>());

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DUNE0001");
    }

    [Fact]
    public async Task DomainRejectsEfInsideGenericContainerArgument()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Features/Tasks/Domain/TaskRules.cs", """
                    using System.Collections.Generic;
                    using Microsoft.EntityFrameworkCore;
                    namespace TemplateApp.Features.Tasks.Domain;
                    public sealed class TaskRules
                    {
                        public List<DbContext> Items { get; } = new();
                    }
                    """),
            },
            AnalyzerHost.Only<DomainDependencyAnalyzer>());

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DUNE0001");
    }

    [Fact]
    public async Task DomainRejectsEfThroughUsingAliasInsideBody()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Features/Tasks/Domain/TaskRules.cs", """
                    using EF = Microsoft.EntityFrameworkCore;
                    namespace TemplateApp.Features.Tasks.Domain;
                    public sealed class TaskRules
                    {
                        public object Check(EF::DbContext context) => context;
                    }
                    """),
            },
            AnalyzerHost.Only<DomainDependencyAnalyzer>());

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DUNE0001");
    }

    [Fact]
    public async Task DomainAcceptsBclInsideMethodLocalAndCast()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Features/Tasks/Domain/TaskRules.cs", """
                    using System;
                    namespace TemplateApp.Features.Tasks.Domain;
                    public sealed class TaskRules
                    {
                        public int Count(string value)
                        {
                            var local = (object)value;
                            return value.Length + ((object)local is string text ? text.Length : 0);
                        }
                    }
                    """),
            },
            AnalyzerHost.Only<DomainDependencyAnalyzer>());

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DUNE0001");
    }

    [Fact]
    public async Task DomainAcceptsOwnDomainMemberAccessInsideBody()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Features/Tasks/Domain/TaskItem.cs", """
                    namespace TemplateApp.Features.Tasks.Domain;
                    public sealed class TaskItem
                    {
                        public string Title { get; set; } = string.Empty;
                    }
                    """),
                ("backend/Features/Tasks/Domain/TaskRules.cs", """
                    namespace TemplateApp.Features.Tasks.Domain;
                    public sealed class TaskRules
                    {
                        public int Measure(TaskItem item) => item.Title.Length;
                    }
                    """),
            },
            AnalyzerHost.Only<DomainDependencyAnalyzer>());

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DUNE0001");
    }

    [Fact]
    public async Task DomainReportsDistinctSpansForRepeatedBodyUse()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Features/Tasks/Domain/TaskRules.cs", """
                    using Microsoft.EntityFrameworkCore;
                    namespace TemplateApp.Features.Tasks.Domain;
                    public sealed class TaskRules
                    {
                        public object Check(DbContext first, DbContext second) => (first, second);
                    }
                    """),
            },
            AnalyzerHost.Only<DomainDependencyAnalyzer>());

        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == "DUNE0001"));
        Assert.Equal(
            diagnostics.Count(diagnostic => diagnostic.Id == "DUNE0001"),
            diagnostics
                .Where(diagnostic => diagnostic.Id == "DUNE0001")
                .Select(diagnostic => diagnostic.Location.SourceSpan.Start)
                .Distinct()
                .Count());
    }

    [Fact]
    public async Task DomainSeesEfTypeOfInsideObjectMethod()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Features/Users/Domain/UserRules.cs", """
                    using System;
                    using Microsoft.EntityFrameworkCore;
                    using TemplateApp.Infrastructure.Persistence;
                    namespace TemplateApp.Features.Users.Domain;
                    public sealed class UserRules
                    {
                        public object Check() => typeof(AppDbContext);
                    }
                    """),
                ("backend/Infrastructure/Persistence/AppDbContext.cs", """
                    using Microsoft.EntityFrameworkCore;
                    namespace TemplateApp.Infrastructure.Persistence;
                    public sealed class AppDbContext : DbContext
                    {
                    }
                    """),
            },
            AnalyzerHost.Only<DomainDependencyAnalyzer>());

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DUNE0001");
    }
}
