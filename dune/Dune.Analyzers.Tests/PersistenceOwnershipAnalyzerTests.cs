using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Dune.Analyzers.Tests;

public sealed class PersistenceOwnershipAnalyzerTests
{
    private const string AuditEntry = """
        namespace TemplateApp.Infrastructure.Persistence;
        public sealed class AuditEntry
        {
            public long Id { get; set; }
        }
        """;

    [Fact]
    public async Task OneContextPerFeaturePersistenceAndTheSharedPiecesInInfrastructureAreAccepted()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Tasks/Persistence/TasksDbContext.cs", """
                using Microsoft.EntityFrameworkCore;
                using TemplateApp.Infrastructure.Persistence;
                namespace TemplateApp.Features.Tasks.Persistence;
                internal sealed class TasksDbContext : DbContext
                {
                    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.MapSharedAuditEntries();
                }
                """),
            ("backend/Features/Users/Persistence/UsersDbContext.cs", """
                using Microsoft.EntityFrameworkCore;
                namespace TemplateApp.Features.Users.Persistence;
                internal sealed class UsersDbContext : DbContext { }
                """),
            ("backend/Infrastructure/Persistence/PlatformDbContext.cs", """
                using Microsoft.EntityFrameworkCore;
                namespace TemplateApp.Infrastructure.Persistence;
                internal sealed class PlatformDbContext : DbContext { }
                """),
            ("backend/Infrastructure/Persistence/AppDbContext.cs", """
                using Microsoft.EntityFrameworkCore;
                namespace TemplateApp.Infrastructure.Persistence;
                internal sealed class AppDbContext : DbContext { }
                """),
            ("backend/Infrastructure/Persistence/SharedAuditEntries.cs", """
                using Microsoft.EntityFrameworkCore;
                namespace TemplateApp.Infrastructure.Persistence;
                public static class SharedAuditEntries
                {
                    public static ModelBuilder MapSharedAuditEntries(this ModelBuilder modelBuilder)
                    {
                        modelBuilder.Entity<AuditEntry>().ToTable(t => t.ExcludeFromMigrations());
                        return modelBuilder;
                    }
                }
                """),
            ("backend/Infrastructure/Persistence/CrossContextTransaction.cs", """
                using System.Transactions;
                namespace TemplateApp.Infrastructure.Persistence;
                public static class CrossContextTransaction
                {
                    public static TransactionScope Begin() =>
                        new(TransactionScopeOption.Required, new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted }, TransactionScopeAsyncFlowOption.Enabled);
                }
                """),
            ("backend/Tests/Support/ScratchDbContext.cs", """
                using System.Transactions;
                using Microsoft.EntityFrameworkCore;
                namespace TemplateApp.Tests.Support;
                public sealed class ScratchDbContext : DbContext
                {
                    public static TransactionScope Scope() => new TransactionScope();
                }
                """));

        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Id == "DUNE0021"));
    }

    [Fact]
    public async Task ContextOutsidePersistenceIsRejected()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Tasks/CreateTask/CreateTaskDbContext.cs", """
                using Microsoft.EntityFrameworkCore;
                namespace TemplateApp.Features.Tasks.CreateTask;
                internal sealed class CreateTaskDbContext : DbContext { }
                """),
            ("backend/Infrastructure/Http/CacheDbContext.cs", """
                using Microsoft.EntityFrameworkCore;
                namespace TemplateApp.Infrastructure.Http;
                internal sealed class CacheDbContext : DbContext { }
                """),
            ("backend/Infrastructure/Persistence/Reports/ReportsDbContext.cs", """
                using Microsoft.EntityFrameworkCore;
                namespace TemplateApp.Infrastructure.Persistence.Reports;
                internal sealed class ReportsDbContext : DbContext { }
                """),
            ("backend/Shared/Domain/SharedDbContext.cs", """
                using Microsoft.EntityFrameworkCore;
                namespace TemplateApp.Shared.Domain;
                internal class SharedDbContext : DbContext { }
                """));

        Assert.Equal(
            new[]
            {
                "DbContext fuera de Persistence CacheDbContext.cs:3",
                "DbContext fuera de Persistence CreateTaskDbContext.cs:3",
                "DbContext fuera de Persistence ReportsDbContext.cs:3",
                "DbContext fuera de Persistence SharedDbContext.cs:3",
            },
            Findings(diagnostics));
    }

    [Fact]
    public async Task SecondContextInAFeatureIsRejected()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Users/Persistence/UsersDbContext.cs", """
                using Microsoft.EntityFrameworkCore;
                namespace TemplateApp.Features.Users.Persistence;
                internal sealed class UsersDbContext : DbContext { }
                """),
            ("backend/Features/Users/Persistence/UsersReadDbContext.cs", """
                using Microsoft.EntityFrameworkCore;
                namespace TemplateApp.Features.Users.Persistence;
                internal sealed class UsersReadDbContext : DbContext { }
                """),
            ("backend/Features/Tasks/Persistence/TasksDbContext.cs", """
                using Microsoft.EntityFrameworkCore;
                namespace TemplateApp.Features.Tasks.Persistence;
                internal sealed class TasksDbContext : DbContext { }
                """));

        Assert.Equal(
            new[]
            {
                "Más de un DbContext en una feature UsersDbContext.cs:3",
                "Más de un DbContext en una feature UsersReadDbContext.cs:3",
            },
            Findings(diagnostics));
        Assert.All(
            diagnostics.Where(diagnostic => diagnostic.Id == "DUNE0021"),
            diagnostic => Assert.Contains("La feature Users tiene 2", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), System.StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExcludeFromMigrationsInAFeatureIsRejected()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Tasks/Persistence/TasksDbContext.cs", """
                using Microsoft.EntityFrameworkCore;
                using TemplateApp.Infrastructure.Persistence;
                namespace TemplateApp.Features.Tasks.Persistence;
                internal sealed class TasksDbContext : DbContext
                {
                    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
                        modelBuilder.Entity<AuditEntry>().ToTable("UserProfiles", t => t.ExcludeFromMigrations());
                }
                """),
            ("backend/Infrastructure/Http/AuditMapping.cs", """
                using Microsoft.EntityFrameworkCore;
                namespace TemplateApp.Infrastructure.Http;
                internal static class AuditMapping
                {
                    public static void Map(ModelBuilder modelBuilder) =>
                        modelBuilder.Entity<TemplateApp.Infrastructure.Persistence.AuditEntry>().ToTable(t => t.ExcludeFromMigrations());
                }
                """));

        Assert.Equal(
            new[]
            {
                "ExcludeFromMigrations fuera de Infrastructure/Persistence AuditMapping.cs:6",
                "ExcludeFromMigrations fuera de Infrastructure/Persistence TasksDbContext.cs:7",
            },
            Findings(diagnostics));
    }

    [Fact]
    public async Task TransactionScopeOutsideTheHelperIsRejected()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Users/DeleteUser/DeleteUser.cs", """
                using System.Transactions;
                namespace TemplateApp.Features.Users.DeleteUser;
                internal sealed class DeleteUser
                {
                    public static void Explicit()
                    {
                        using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
                        scope.Complete();
                    }

                    public static void TargetTyped()
                    {
                        using TransactionScope scope = new(TransactionScopeOption.Required);
                        scope.Complete();
                    }
                }
                """),
            ("backend/Infrastructure/Http/IdempotencyFilter.cs", """
                namespace TemplateApp.Infrastructure.Http;
                internal static class IdempotencyFilter
                {
                    public static System.Transactions.TransactionScope Begin() => new System.Transactions.TransactionScope();
                }
                """));

        Assert.Equal(
            new[]
            {
                "TransactionScope fuera de CrossContextTransaction DeleteUser.cs:13",
                "TransactionScope fuera de CrossContextTransaction DeleteUser.cs:7",
                "TransactionScope fuera de CrossContextTransaction IdempotencyFilter.cs:4",
            },
            Findings(diagnostics));
    }

    [Fact]
    public async Task TwoContextsWithTheSameNameInAFeatureAreTwoContexts()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Users/Persistence/StoreContext.cs", """
                using Microsoft.EntityFrameworkCore;
                namespace TemplateApp.Features.Users.Persistence;
                internal sealed class StoreContext : DbContext { }
                """),
            ("backend/Features/Users/Persistence/ReadStoreContext.cs", """
                using Microsoft.EntityFrameworkCore;
                namespace TemplateApp.Features.Users.Persistence.Read;
                internal sealed class StoreContext : DbContext { }
                """));

        Assert.Equal(
            new[]
            {
                "Más de un DbContext en una feature ReadStoreContext.cs:3",
                "Más de un DbContext en una feature StoreContext.cs:3",
            },
            Findings(diagnostics));
    }

    [Fact]
    public async Task AnAbstractBaseIsNotASecondContext()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Users/Persistence/AuditedDbContext.cs", """
                using Microsoft.EntityFrameworkCore;
                namespace TemplateApp.Features.Users.Persistence;
                internal abstract class AuditedDbContext : DbContext { }
                """),
            ("backend/Features/Users/Persistence/UsersDbContext.cs", """
                namespace TemplateApp.Features.Users.Persistence;
                internal sealed class UsersDbContext : AuditedDbContext { }
                """));

        Assert.Empty(Findings(diagnostics));
    }

    [Fact]
    public async Task TransactionScopeElsewhereInInfrastructurePersistenceIsRejected()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Infrastructure/Persistence/IdempotencyKeys.cs", """
                using System.Transactions;
                namespace TemplateApp.Infrastructure.Persistence;
                internal static class IdempotencyKeys
                {
                    public static TransactionScope Begin() => new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
                }
                """));

        Assert.Equal(
            new[] { "TransactionScope fuera de CrossContextTransaction IdempotencyKeys.cs:5" },
            Findings(diagnostics));
    }

    [Fact]
    public async Task AContextAppliesOnlyConfigurationsOfItsOwner()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Infrastructure/Persistence/IdempotencyKeyConfiguration.cs", """
                using Microsoft.EntityFrameworkCore;
                using Microsoft.EntityFrameworkCore.Metadata.Builders;
                namespace TemplateApp.Infrastructure.Persistence;
                public sealed class IdempotencyKeyConfiguration : IEntityTypeConfiguration<AuditEntry>
                {
                    public void Configure(EntityTypeBuilder<AuditEntry> builder) { }
                }
                """),
            ("backend/Infrastructure/Persistence/PlatformDbContext.cs", """
                using Microsoft.EntityFrameworkCore;
                namespace TemplateApp.Infrastructure.Persistence;
                internal sealed class PlatformDbContext : DbContext
                {
                    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
                        modelBuilder.ApplyConfiguration(new IdempotencyKeyConfiguration());
                }
                """),
            ("backend/Features/Tasks/Persistence/TaskItemConfiguration.cs", """
                using Microsoft.EntityFrameworkCore;
                using Microsoft.EntityFrameworkCore.Metadata.Builders;
                using TemplateApp.Infrastructure.Persistence;
                namespace TemplateApp.Features.Tasks.Persistence;
                internal sealed class TaskItemConfiguration : IEntityTypeConfiguration<AuditEntry>
                {
                    public void Configure(EntityTypeBuilder<AuditEntry> builder) { }
                }
                """),
            ("backend/Features/Tasks/Persistence/TasksDbContext.cs", """
                using Microsoft.EntityFrameworkCore;
                using TemplateApp.Infrastructure.Persistence;
                namespace TemplateApp.Features.Tasks.Persistence;
                internal sealed class TasksDbContext : DbContext
                {
                    protected override void OnModelCreating(ModelBuilder modelBuilder)
                    {
                        modelBuilder.ApplyConfiguration(new TaskItemConfiguration());
                        modelBuilder.ApplyConfiguration(new IdempotencyKeyConfiguration());
                        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TasksDbContext).Assembly);
                    }
                }
                """));

        Assert.Equal(
            new[]
            {
                "Configuración de otro dueño TasksDbContext.cs:10",
                "Configuración de otro dueño TasksDbContext.cs:9",
            },
            Findings(diagnostics));
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(params (string Path, string Source)[] files) =>
        AnalyzerHost.AnalyzeAsync(
            files.Append(("backend/Infrastructure/Persistence/AuditEntry.cs", AuditEntry)).ToList(),
            AnalyzerHost.Only<PersistenceOwnershipAnalyzer>());

    private static string[] Findings(ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics
            .Where(diagnostic => diagnostic.Id == "DUNE0021")
            .Select(diagnostic =>
            {
                var span = diagnostic.Location.GetLineSpan();
                return $"{diagnostic.Descriptor.Title} {System.IO.Path.GetFileName(span.Path)}:{span.StartLinePosition.Line + 1}";
            })
            .OrderBy(finding => finding, System.StringComparer.Ordinal)
            .ToArray();
}
