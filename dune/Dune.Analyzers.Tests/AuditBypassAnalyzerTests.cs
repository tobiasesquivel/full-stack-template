using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Dune.Analyzers.Tests;

public sealed class AuditBypassAnalyzerTests
{
    private const string Model = """
        using Microsoft.EntityFrameworkCore;
        namespace TemplateApp.Shared.Domain
        {
            public interface IAuditable { }
        }
        namespace TemplateApp.Features.Tasks.Domain
        {
            public sealed class TaskItem : TemplateApp.Shared.Domain.IAuditable
            {
                public System.Guid Id { get; set; }
                public bool IsCompleted { get; set; }
            }
        }
        namespace TemplateApp.Features.Users.Domain
        {
            public sealed class UserProfile : TemplateApp.Shared.Domain.IAuditable
            {
                public string AuthId { get; set; } = "";
                public string DisplayName { get; set; } = "";
            }
        }
        namespace TemplateApp.Features.Auth.Domain
        {
            public sealed class Session
            {
                public System.Guid Id { get; set; }
            }
        }
        namespace TemplateApp.Infrastructure.Persistence
        {
            public sealed class AppDbContext : DbContext
            {
                public DbSet<TemplateApp.Features.Tasks.Domain.TaskItem> TaskItems => Set<TemplateApp.Features.Tasks.Domain.TaskItem>();
                public DbSet<TemplateApp.Features.Auth.Domain.Session> Sessions => Set<TemplateApp.Features.Auth.Domain.Session>();
            }
        }
        """;

    [Fact]
    public async Task BulkWritesOnNonAuditedEntitiesAndSaveChangesOnAuditedOnesAreAccepted()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Auth/Access/Sessions.cs", """
                using System;
                using System.Linq;
                using System.Threading;
                using System.Threading.Tasks;
                using Microsoft.EntityFrameworkCore;
                using TemplateApp.Infrastructure.Persistence;
                namespace TemplateApp.Features.Auth.Access;
                public sealed class Sessions(AppDbContext db)
                {
                    public Task<int> CloseAsync(Guid id, CancellationToken ct) =>
                        db.Sessions.Where(s => s.Id == id).ExecuteDeleteAsync(ct);
                }
                """),
            ("backend/Features/Tasks/DeleteTask/DeleteTask.cs", """
                using System;
                using System.Threading;
                using System.Threading.Tasks;
                using Microsoft.EntityFrameworkCore;
                using TemplateApp.Infrastructure.Persistence;
                namespace TemplateApp.Features.Tasks.DeleteTask;
                public sealed class DeleteTask(AppDbContext db)
                {
                    public async Task ExecuteAsync(Guid id, CancellationToken ct)
                    {
                        var item = await db.TaskItems.SingleAsync(t => t.Id == id, ct);
                        db.TaskItems.Remove(item);
                        await db.SaveChangesAsync(ct);
                    }
                }
                """),
            ("backend/Features/Tasks/DeleteTask/DeleteTaskTests.cs", """
                using System.Threading.Tasks;
                using Microsoft.EntityFrameworkCore;
                using TemplateApp.Infrastructure.Persistence;
                namespace TemplateApp.Features.Tasks.DeleteTask;
                public sealed class DeleteTaskTests
                {
                    public Task<int> ResetAsync(AppDbContext db) => db.TaskItems.ExecuteDeleteAsync();
                }
                """));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task BulkWritesOnAuditedEntitiesAreRejected()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Tasks/DeleteTask/DeleteTask.cs", """
                using System;
                using System.Linq;
                using System.Threading;
                using System.Threading.Tasks;
                using Microsoft.EntityFrameworkCore;
                using TemplateApp.Infrastructure.Persistence;
                namespace TemplateApp.Features.Tasks.DeleteTask;
                public sealed class DeleteTask(AppDbContext db)
                {
                    public Task<int> ExecuteAsync(Guid id, CancellationToken ct) =>
                        db.TaskItems.Where(t => t.Id == id).ExecuteDeleteAsync(ct);
                }
                """),
            ("backend/Features/Users/UpdateProfile/UpdateProfile.cs", """
                using System.Linq;
                using Microsoft.EntityFrameworkCore;
                using TemplateApp.Features.Users.Domain;
                using TemplateApp.Infrastructure.Persistence;
                namespace TemplateApp.Features.Users.UpdateProfile;
                public sealed class UpdateProfile(AppDbContext db)
                {
                    public int Execute(string authId, string name)
                    {
                        var profiles = db.Set<UserProfile>().Where(p => p.AuthId == authId);
                        return profiles.ExecuteUpdate(s => s.SetProperty(p => p.DisplayName, name));
                    }
                }
                """));

        Assert.Equal(
            new[]
            {
                ("backend/Features/Tasks/DeleteTask/DeleteTask.cs", 11),
                ("backend/Features/Users/UpdateProfile/UpdateProfile.cs", 11),
            },
            Locations(diagnostics));
    }

    private static Task<System.Collections.Immutable.ImmutableArray<Diagnostic>> AnalyzeAsync(
        params (string Path, string Source)[] files) =>
        AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>(files) { ("backend/Model.cs", Model) },
            AnalyzerHost.Only<AuditBypassAnalyzer>());

    private static (string Path, int Line)[] Locations(System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics
            .Where(diagnostic => diagnostic.Id == "DUNE0017")
            .Select(diagnostic => (
                diagnostic.Location.SourceTree!.FilePath,
                diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1))
            .OrderBy(location => location.FilePath, System.StringComparer.Ordinal)
            .ThenBy(location => location.Item2)
            .ToArray();
}
