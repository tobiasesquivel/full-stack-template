using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Dune.Analyzers.Tests;

public sealed class TransactionOwnerAnalyzerTests
{
    private const string Platform = """
        using System.Transactions;
        namespace TemplateApp.Infrastructure.Persistence.Transactions
        {
            public static class CrossContextTransaction
            {
                public static TransactionScope Begin() => new(TransactionScopeAsyncFlowOption.Enabled);
            }
        }
        namespace TemplateApp.Infrastructure.Messaging
        {
            public enum MediatorTransaction
            {
                None,
                Atomic,
                Eager,
            }

            public static class MessagingRegistration
            {
                public static object AddMediatorHandler<THandler>(this object services, MediatorTransaction transaction)
                    where THandler : class => services;
            }
        }
        namespace TemplateApp.Features.Projects.Persistence
        {
            public sealed class ProjectsDbContext : Microsoft.EntityFrameworkCore.DbContext
            {
            }
        }
        namespace TemplateApp.Features.Projects
        {
            public static class ProjectsFeature
            {
                public static object AddProjectsFeature(object services)
                {
                    TemplateApp.Infrastructure.Messaging.MessagingRegistration.AddMediatorHandler<CreateProject.CreateProject.Handler>(services, TemplateApp.Infrastructure.Messaging.MediatorTransaction.Atomic);
                    TemplateApp.Infrastructure.Messaging.MessagingRegistration.AddMediatorHandler<RenameProject.RenameProject.Handler>(services, TemplateApp.Infrastructure.Messaging.MediatorTransaction.Atomic);
                    return services;
                }
            }
        }
        """;

    [Fact]
    public async Task AHandler_LeavesTheTransactionToWolverine_AndSavesOnlyToTranslateAConstraint()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Projects/CreateProject/CreateProject.cs", """
                using System.Threading;
                using System.Threading.Tasks;
                using Microsoft.EntityFrameworkCore;
                using TemplateApp.Features.Projects.Persistence;
                namespace TemplateApp.Features.Projects.CreateProject;
                public static class CreateProject
                {
                    public sealed class Handler(ProjectsDbContext db)
                    {
                        public async Task<int> Handle(string name, CancellationToken ct)
                        {
                            try
                            {
                                await db.SaveChangesAsync(ct);
                            }
                            catch (DbUpdateConcurrencyException)
                            {
                                return 409;
                            }

                            return 201;
                        }
                    }
                }
                """),
            ("backend/Features/Projects/RenameProject/RenameProject.cs", """
                using System.Threading;
                using System.Threading.Tasks;
                using TemplateApp.Features.Projects.Persistence;
                namespace TemplateApp.Features.Projects.RenameProject;
                public static class RenameProject
                {
                    public sealed class Handler(ProjectsDbContext db)
                    {
                        public Task<int> Handle(string name, CancellationToken ct) => Task.FromResult(name.Length);
                    }
                }
                """),
            ("backend/Features/Projects/ProjectsModule.cs", """
                using System.Threading;
                using System.Threading.Tasks;
                using TemplateApp.Features.Projects.Persistence;
                namespace TemplateApp.Features.Projects;
                public sealed class ProjectsModule(ProjectsDbContext db)
                {
                    public Task DeleteAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
                }
                """));

        Assert.Empty(Locations(diagnostics));
    }

    [Fact]
    public async Task AHandler_ThatSavesLoose_OrOpensOrCommitsATransaction_IsRejected()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Projects/CreateProject/CreateProject.cs", """
                using System.Threading;
                using System.Threading.Tasks;
                using System.Transactions;
                using Microsoft.EntityFrameworkCore;
                using TemplateApp.Features.Projects.Persistence;
                using TemplateApp.Infrastructure.Persistence.Transactions;
                namespace TemplateApp.Features.Projects.CreateProject;
                public static class CreateProject
                {
                    public sealed class Handler(ProjectsDbContext db)
                    {
                        public async Task<int> Handle(string name, CancellationToken ct)
                        {
                            await db.SaveChangesAsync(ct);
                            using TransactionScope scope = CrossContextTransaction.Begin();
                            scope.Complete();
                            return 201;
                        }
                    }
                }
                """),
            ("backend/Features/Projects/RenameProject/RenameProject.cs", """
                using System.Threading;
                using System.Threading.Tasks;
                using Microsoft.EntityFrameworkCore;
                using Microsoft.EntityFrameworkCore.Storage;
                using TemplateApp.Features.Projects.Persistence;
                namespace TemplateApp.Features.Projects.RenameProject;
                public static class RenameProject
                {
                    public sealed class Handler(ProjectsDbContext db)
                    {
                        public async Task<int> Handle(string name, CancellationToken ct)
                        {
                            await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
                            try
                            {
                                db.SaveChanges();
                            }
                            catch (System.InvalidOperationException)
                            {
                                return 500;
                            }

                            await transaction.CommitAsync(ct);
                            return 200;
                        }
                    }
                }
                """));

        Assert.Equal(
            new[]
            {
                ("backend/Features/Projects/CreateProject/CreateProject.cs", 14),
                ("backend/Features/Projects/CreateProject/CreateProject.cs", 15),
                ("backend/Features/Projects/RenameProject/RenameProject.cs", 13),
                ("backend/Features/Projects/RenameProject/RenameProject.cs", 16),
                ("backend/Features/Projects/RenameProject/RenameProject.cs", 23),
            },
            Locations(diagnostics));
    }

    private static Task<System.Collections.Immutable.ImmutableArray<Diagnostic>> AnalyzeAsync(
        params (string Path, string Source)[] files) =>
        AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>(files) { ("backend/Platform.cs", Platform) },
            AnalyzerHost.Only<TransactionOwnerAnalyzer>());

    private static (string Path, int Line)[] Locations(System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics
            .Where(diagnostic => diagnostic.Id == "DUNE0029")
            .Select(diagnostic => (
                diagnostic.Location.SourceTree!.FilePath,
                diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1))
            .OrderBy(location => location.FilePath, System.StringComparer.Ordinal)
            .ThenBy(location => location.Item2)
            .ToArray();
}
