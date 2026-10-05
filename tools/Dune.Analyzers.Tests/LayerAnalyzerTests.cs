using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Dune.Analyzers.Tests;

// DUNE0001 through DUNE0004 observe diagnostics through the real analyzer
// pipeline. Each negative case must produce the expected id; each positive
// case must stay silent. Duplicated assertions across rules would couple the
// tests to incidental overlap, so one rule owns each fixture file.
public sealed class LayerAnalyzerTests
{
    [Fact]
    public async Task DomainRejectsEfReference()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Features/Tasks/Domain/TaskItem.cs", """
                    using Microsoft.EntityFrameworkCore;
                    namespace TemplateApp.Features.Tasks.Domain;
                    public sealed class TaskItem
                    {
                        public DbContext? Context { get; set; }
                    }
                    """),
            },
            AnalyzerHost.Only<DomainDependencyAnalyzer>());

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DUNE0001");
    }

    [Fact]
    public async Task DomainRejectsAspNetReferenceThroughAlias()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Features/Users/Domain/UserProfile.cs", """
                    namespace TemplateApp.Features.Users.Domain;
                    public sealed class UserProfile
                    {
                        public Microsoft.AspNetCore.Identity.IdentityUser<string>? Identity { get; set; }
                    }
                    """),
            },
            AnalyzerHost.Only<DomainDependencyAnalyzer>());

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DUNE0001");
    }

    [Fact]
    public async Task DomainAcceptsSharedDomainAndOwnDomain()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Shared/Domain/IOwnedResource.cs", """
                    namespace TemplateApp.Shared.Domain;
                    public interface IOwnedResource { string OwnerId { get; } }
                    """),
                ("backend/Features/Tasks/Domain/TaskItem.cs", """
                    using System;
                    using TemplateApp.Shared.Domain;
                    namespace TemplateApp.Features.Tasks.Domain;
                    public sealed class TaskItem : IOwnedResource
                    {
                        public Guid Id { get; set; }
                        public string OwnerId { get; set; } = string.Empty;
                        public string Title { get; set; } = string.Empty;
                    }
                    """),
            },
            AnalyzerHost.Only<DomainDependencyAnalyzer>());

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DUNE0001");
    }

    [Fact]
    public async Task EndpointRejectsDirectDbContextUse()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Features/Tasks/TasksEndpoints.cs", """
                    using Microsoft.EntityFrameworkCore;
                    namespace TemplateApp.Features.Tasks;
                    public sealed class TasksEndpoints
                    {
                        public void Map(DbContext context) => _ = context;
                    }
                    """),
            },
            AnalyzerHost.Only<EndpointLayerAnalyzer>());

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DUNE0002");
    }

    [Fact]
    public async Task EndpointRejectsEfTypeReferenceThroughAlias()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Features/Tasks/TasksAdminEndpoints.cs", """
                    using EF = Microsoft.EntityFrameworkCore;
                    namespace TemplateApp.Features.Tasks;
                    public sealed class TasksAdminEndpoints
                    {
                        public EF::DbSet<string>? Items { get; set; }
                    }
                    """),
            },
            AnalyzerHost.Only<EndpointLayerAnalyzer>());

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DUNE0002");
    }

    [Fact]
    public async Task EndpointRejectsUserManagerThroughAlias()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Features/Auth/AuthEndpoints.cs", """
                    using Identity = Microsoft.AspNetCore.Identity;
                    namespace TemplateApp.Features.Auth;
                    public sealed class AuthEndpoints
                    {
                        public void Map<TUser>(Identity::UserManager<TUser> users) where TUser : class => _ = users;
                    }
                    """),
            },
            AnalyzerHost.Only<EndpointLayerAnalyzer>());

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DUNE0002");
    }

    [Fact]
    public async Task InfrastructureRejectsModuleReferenceOutsideComposition()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Features/Tasks/TasksModule.cs", """
                    namespace TemplateApp.Features.Tasks;
                    public sealed class TasksModule { }
                    """),
                ("backend/Infrastructure/Caching/TasksCache.cs", """
                    using TemplateApp.Features.Tasks;
                    namespace TemplateApp.Infrastructure.Caching;
                    public sealed class TasksCache
                    {
                        public TasksModule? Module { get; set; }
                    }
                    """),
            },
            AnalyzerHost.Only<InfrastructureBoundaryAnalyzer>());

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DUNE0003");
    }

    [Fact]
    public async Task CompositionSeamMayWireModules()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Features/Tasks/TasksModule.cs", """
                    namespace TemplateApp.Features.Tasks;
                    public sealed class TasksModule { }
                    """),
                ("backend/Infrastructure/DependencyInjection/BackendInfrastructure.cs", """
                    using TemplateApp.Features.Tasks;
                    namespace TemplateApp.Infrastructure.DependencyInjection;
                    public static class Composition
                    {
                        public static object AddBackendInfrastructure(TasksModule module) => module;
                    }
                    """),
            },
            AnalyzerHost.Only<InfrastructureBoundaryAnalyzer>());

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DUNE0003");
    }

    [Fact]
    public async Task CrossFeatureRejectsForeignImplementation()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Features/Users/UsersModule.cs", """
                    namespace TemplateApp.Features.Users;
                    public sealed class UsersModule { }
                    """),
                ("backend/Features/Tasks/CreateTask.cs", """
                    using TemplateApp.Features.Users;
                    namespace TemplateApp.Features.Tasks;
                    public sealed class CreateTask
                    {
                        public UsersModule? Users { get; set; }
                    }
                    """),
            },
            AnalyzerHost.Only<CrossFeatureAnalyzer>());

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DUNE0004");
    }

    [Fact]
    public async Task CrossFeatureAcceptsForeignDomain()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Features/Users/Domain/UserProfile.cs", """
                    namespace TemplateApp.Features.Users.Domain;
                    public sealed class UserProfile { }
                    """),
                ("backend/Features/Tasks/CreateTask.cs", """
                    using TemplateApp.Features.Users.Domain;
                    namespace TemplateApp.Features.Tasks;
                    public sealed class CreateTask
                    {
                        public UserProfile? Profile { get; set; }
                    }
                    """),
            },
            AnalyzerHost.Only<CrossFeatureAnalyzer>());

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DUNE0004");
    }
}
