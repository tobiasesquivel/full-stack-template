using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Dune.Analyzers.Tests;

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
    public async Task CompositionInInfrastructureIsRejected()
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

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DUNE0003");
    }

    [Fact]
    public async Task InfrastructureRejectsFeatureRegistrationAndServices()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Features/Auth/AuthFeature.cs", """
                    namespace TemplateApp.Features.Auth;
                    public static class AuthFeature
                    {
                        public static int AddAuthFeature(this string services) => services.Length;
                    }
                    """),
                ("backend/Features/Auth/Access/RoleSeeder.cs", """
                    namespace TemplateApp.Features.Auth.Access;
                    public sealed class RoleSeeder { }
                    """),
                ("backend/Infrastructure/Persistence/DatabaseInitializer.cs", """
                    using TemplateApp.Features.Auth;
                    namespace TemplateApp.Infrastructure.Persistence;
                    public static class DatabaseInitializer
                    {
                        public static int Seed(string services) => services.AddAuthFeature();
                        public static object Create() => new TemplateApp.Features.Auth.Access.RoleSeeder();
                    }
                    """),
            },
            AnalyzerHost.Only<InfrastructureBoundaryAnalyzer>());

        var messages = diagnostics
            .Where(diagnostic => diagnostic.Id == "DUNE0003")
            .Select(diagnostic => diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture))
            .ToList();
        Assert.Contains(messages, message => message.Contains("AddAuthFeature", System.StringComparison.Ordinal));
        Assert.Contains(messages, message => message.Contains("RoleSeeder", System.StringComparison.Ordinal));
    }

    [Fact]
    public async Task InfrastructureAcceptsFeatureDomainAndContracts()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Features/Tasks/Domain/TaskItem.cs", """
                    namespace TemplateApp.Features.Tasks.Domain;
                    public sealed class TaskItem { }
                    """),
                ("backend/Features/Auth/Contracts/AppIdentityUser.cs", """
                    namespace TemplateApp.Features.Auth.Contracts;
                    public sealed class AppIdentityUser { }
                    """),
                ("backend/Infrastructure/Persistence/AppDbContext.cs", """
                    using TemplateApp.Features.Auth.Contracts;
                    using TemplateApp.Features.Tasks.Domain;
                    namespace TemplateApp.Infrastructure.Persistence;
                    public sealed class AppDbContext
                    {
                        public TaskItem? Task { get; set; }
                        public AppIdentityUser? User { get; set; }
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
    public async Task CrossFeatureRejectsForeignDomain()
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

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DUNE0004");
    }

    [Fact]
    public async Task CrossFeatureRejectsForeignDomainReachedWithoutNamingIt()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Features/Users/Domain/UserProfile.cs", """
                    namespace TemplateApp.Features.Users.Domain;
                    public sealed class UserProfile
                    {
                        public string DisplayName { get; set; } = "";
                    }
                    """),
                ("backend/Infrastructure/Persistence/Store.cs", """
                    using System.Collections.Generic;
                    using TemplateApp.Features.Users.Domain;
                    namespace TemplateApp.Infrastructure.Persistence;
                    public sealed class Store
                    {
                        public List<UserProfile> Profiles { get; } = new();
                    }
                    """),
                ("backend/Features/Auth/OpenSession/OpenSession.cs", """
                    using TemplateApp.Infrastructure.Persistence;
                    namespace TemplateApp.Features.Auth.OpenSession;
                    public sealed class OpenSession
                    {
                        public string Read(Store store)
                        {
                            var profile = store.Profiles[0];
                            return profile.DisplayName;
                        }
                    }
                    """),
            },
            AnalyzerHost.Only<CrossFeatureAnalyzer>());

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DUNE0004"
            && diagnostic.Location.SourceTree!.FilePath.EndsWith("OpenSession.cs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CrossFeatureAcceptsForeignContractsAndOwnDomain()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Features/Users/Contracts/IUserProfiles.cs", """
                    namespace TemplateApp.Features.Users.Contracts;
                    public interface IUserProfiles
                    {
                        UserProfileSnapshot? Find(string authId);
                    }
                    """),
                ("backend/Features/Users/Contracts/UserProfileSnapshot.cs", """
                    namespace TemplateApp.Features.Users.Contracts;
                    public sealed record UserProfileSnapshot(string DisplayName, string Timezone);
                    """),
                ("backend/Features/Auth/Domain/RefreshToken.cs", """
                    namespace TemplateApp.Features.Auth.Domain;
                    public sealed class RefreshToken { }
                    """),
                ("backend/Features/Auth/OpenSession/OpenSession.cs", """
                    using TemplateApp.Features.Auth.Domain;
                    using TemplateApp.Features.Users.Contracts;
                    namespace TemplateApp.Features.Auth.OpenSession;
                    public sealed class OpenSession
                    {
                        public RefreshToken? Token { get; set; }
                        public string Read(IUserProfiles profiles)
                        {
                            var profile = profiles.Find("id");
                            return profile?.DisplayName ?? "";
                        }
                    }
                    """),
            },
            AnalyzerHost.Only<CrossFeatureAnalyzer>());

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DUNE0004");
    }

    [Fact]
    public async Task EndpointInMixedFileAcceptsModuleEfWhileAdapterStaysClean()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Features/Tasks/CreateTask.cs", """
                    using Microsoft.EntityFrameworkCore;
                    namespace TemplateApp.Features.Tasks;
                    public sealed partial class TasksModule
                    {
                        private sealed class Store : DbContext
                        {
                            public Store(DbContextOptions<Store> options) : base(options) { }
                        }
                        private void Touch(Store store) => _ = store;
                    }
                    public static partial class TasksEndpoints
                    {
                        public static string Ping() => "pong";
                    }
                    """),
            },
            AnalyzerHost.Only<EndpointLayerAnalyzer>());

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DUNE0002");
    }

    [Fact]
    public async Task EndpointInMixedFileRejectsPersistenceInsideEndpointDeclaration()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Features/Tasks/CreateTask.cs", """
                    using Microsoft.EntityFrameworkCore;
                    namespace TemplateApp.Features.Tasks;
                    public sealed partial class TasksModule
                    {
                        public void Touch(DbContext context) => _ = context;
                    }
                    public static partial class TasksEndpoints
                    {
                        public sealed class Nested
                        {
                            public void Map(DbContext context) => _ = context;
                        }
                    }
                    """),
            },
            AnalyzerHost.Only<EndpointLayerAnalyzer>());

        var violation = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "DUNE0002");
        var lineSpan = violation.Location.GetLineSpan();
        Assert.Equal("backend/Features/Tasks/CreateTask.cs", lineSpan.Path.Replace('\\', '/'));
        Assert.Equal(10, lineSpan.StartLinePosition.Line);
    }
}
