using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Dune.Analyzers.Tests;

public sealed class NamingAndQueryAnalyzerTests
{
    [Fact]
    public async Task NamespaceMustMatchPath()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Features/Tasks/Create/CreateTaskRequest.cs", """
                    namespace TemplateApp.Features.Auth;
                    public sealed class CreateTaskRequest { }
                    """),
            },
            AnalyzerHost.Only<NamingAnalyzer>());

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DUNE0005");
    }

    [Fact]
    public async Task ModuleOutsideFeatureRootIsRejected()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Features/Tasks/Create/TasksModule.cs", """
                    namespace TemplateApp.Features.Tasks.Create;
                    public sealed class TasksModule { }
                    """),
            },
            AnalyzerHost.Only<NamingAnalyzer>());

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DUNE0008");
    }

    [Fact]
    public async Task ContractsInDomainAreRejected()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Features/Tasks/Domain/TaskResponse.cs", """
                    namespace TemplateApp.Features.Tasks.Domain;
                    public sealed record TaskResponse(string Title);
                    """),
            },
            AnalyzerHost.Only<NamingAnalyzer>());

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DUNE0008");
    }

    [Fact]
    public async Task NewDbSetShadowingIdentityUsersIsRejected()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Infrastructure/Persistence/AppDbContext.cs", """
                    using Microsoft.EntityFrameworkCore;
                    namespace TemplateApp.Infrastructure.Persistence;
                    public class BaseContext : DbContext
                    {
                        public DbSet<string> Users => Set<string>();
                    }
                    public sealed class AppDbContext : BaseContext
                    {
                        public new DbSet<string> Users => Set<string>();
                    }
                    """),
            },
            AnalyzerHost.Only<DbSetShadowAnalyzer>());

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DUNE0006");
    }

    [Fact]
    public async Task NewNonDbSetPropertyIsAccepted()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Infrastructure/Persistence/AppDbContext.cs", """
                    namespace TemplateApp.Infrastructure.Persistence;
                    public class BaseContext
                    {
                        public string Name => "base";
                    }
                    public sealed class DerivedContext : BaseContext
                    {
                        public new string Name => "derived";
                    }
                    """),
            },
            AnalyzerHost.Only<DbSetShadowAnalyzer>());

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DUNE0006");
    }

    [Fact]
    public async Task SingleLookupWithIdAndOwnerIsRejected()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Features/Tasks/GetTaskById.cs", """
                    using System;
                    using System.Linq;
                    namespace TemplateApp.Features.Tasks;
                    public sealed class TaskItem
                    {
                        public Guid Id { get; set; }
                        public string OwnerId { get; set; } = string.Empty;
                    }
                    public sealed class GetTaskById
                    {
                        public TaskItem? Load(IQueryable<TaskItem> items, Guid id, string owner)
                            => items.Where(t => t.Id == id && t.OwnerId == owner).FirstOrDefault();
                    }
                    """),
            },
            AnalyzerHost.Only<OwnerPredicateAnalyzer>());

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DUNE0007");
    }

    [Fact]
    public async Task ListFilteredByOwnerIsAccepted()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Features/Tasks/ListTasks.cs", """
                    using System.Linq;
                    namespace TemplateApp.Features.Tasks;
                    public sealed class TaskItem
                    {
                        public string OwnerId { get; set; } = string.Empty;
                    }
                    public sealed class ListTasks
                    {
                        public IQueryable<TaskItem> Load(IQueryable<TaskItem> items, string owner)
                            => items.Where(t => t.OwnerId == owner);
                    }
                    """),
            },
            AnalyzerHost.Only<OwnerPredicateAnalyzer>());

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DUNE0007");
    }

    [Fact]
    public async Task SingleLookupByIdOnlyIsAccepted()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Features/Tasks/GetTaskById.cs", """
                    using System;
                    using System.Linq;
                    namespace TemplateApp.Features.Tasks;
                    public sealed class ValidTaskItem
                    {
                        public Guid Id { get; set; }
                    }
                    public sealed class GetValidTaskById
                    {
                        public ValidTaskItem? Load(IQueryable<ValidTaskItem> items, Guid id)
                            => items.Where(t => t.Id == id).FirstOrDefault();
                    }
                    """),
            },
            AnalyzerHost.Only<OwnerPredicateAnalyzer>());

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DUNE0007");
    }
}
