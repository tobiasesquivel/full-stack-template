using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Dune.Analyzers.Tests;

public sealed class ReadSliceAnalyzerTests
{
    private const string Model = """
        using System;
        using Microsoft.EntityFrameworkCore;
        namespace Microsoft.AspNetCore.Routing
        {
            public interface IEndpointRouteBuilder { }
        }
        namespace Microsoft.AspNetCore.Builder
        {
            using Microsoft.AspNetCore.Routing;
            public sealed class RouteHandlerBuilder { }
            public static class EndpointRouteBuilderExtensions
            {
                public static RouteHandlerBuilder MapGet(this IEndpointRouteBuilder endpoints, string pattern, Delegate handler) => new();
                public static RouteHandlerBuilder MapPut(this IEndpointRouteBuilder endpoints, string pattern, Delegate handler) => new();
            }
        }
        namespace TemplateApp.Features.Tasks.Domain
        {
            public sealed class TaskItem
            {
                public Guid Id { get; set; }
                public string OwnerId { get; set; } = "";
                public string Title { get; set; } = "";
            }
        }
        namespace TemplateApp.Features.Users.Domain
        {
            public sealed class UserProfile
            {
                public string AuthId { get; set; } = "";
            }
        }
        namespace TemplateApp.Infrastructure.Persistence
        {
            public sealed class AppDbContext : DbContext
            {
                public DbSet<TemplateApp.Features.Tasks.Domain.TaskItem> TaskItems => Set<TemplateApp.Features.Tasks.Domain.TaskItem>();
                public DbSet<TemplateApp.Features.Users.Domain.UserProfile> UserProfiles => Set<TemplateApp.Features.Users.Domain.UserProfile>();
            }
        }
        """;

    private static (string Path, string Source) Endpoint(string feature, string slice, string verb) =>
        ($"backend/Features/{feature}/{slice}/{slice}Endpoint.cs", $$"""
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Routing;
            namespace TemplateApp.Features.{{feature}}.{{slice}};
            public static class {{slice}}Endpoint
            {
                public static void Map(IEndpointRouteBuilder routes) => routes.{{verb}}("/", () => 0);
            }
            """);

    [Fact]
    public async Task ProjectingReadsWritingSlicesTestsAndTheGetUserExceptionAreAccepted()
    {
        var diagnostics = await AnalyzeAsync(
            Endpoint("Tasks", "GetTask", "MapGet"),
            ("backend/Features/Tasks/GetTask/GetTask.cs", """
                using System;
                using System.Linq;
                using System.Threading;
                using System.Threading.Tasks;
                using Microsoft.EntityFrameworkCore;
                using TemplateApp.Infrastructure.Persistence;
                namespace TemplateApp.Features.Tasks.GetTask;
                public sealed record TaskView(Guid Id, string Title);
                public sealed class GetTask(AppDbContext db)
                {
                    public Task<TaskView?> ExecuteAsync(Guid id, CancellationToken ct) =>
                        db.TaskItems.Where(t => t.Id == id).Select(t => new TaskView(t.Id, t.Title)).SingleOrDefaultAsync(ct);
                }
                """),
            ("backend/Features/Tasks/GetTask/GetTaskTests.cs", """
                using System.Threading.Tasks;
                using Microsoft.EntityFrameworkCore;
                using TemplateApp.Infrastructure.Persistence;
                namespace TemplateApp.Features.Tasks.GetTask;
                public sealed class GetTaskTests
                {
                    public async Task SeedAsync(AppDbContext db)
                    {
                        await db.TaskItems.ToListAsync();
                        await db.SaveChangesAsync();
                    }
                }
                """),
            Endpoint("Tasks", "UpdateTask", "MapPut"),
            ("backend/Features/Tasks/UpdateTask/UpdateTask.cs", """
                using System;
                using System.Threading;
                using System.Threading.Tasks;
                using Microsoft.EntityFrameworkCore;
                using TemplateApp.Infrastructure.Persistence;
                namespace TemplateApp.Features.Tasks.UpdateTask;
                public sealed class UpdateTask(AppDbContext db)
                {
                    public async Task ExecuteAsync(Guid id, string title, CancellationToken ct)
                    {
                        var item = await db.TaskItems.SingleAsync(t => t.Id == id, ct);
                        item.Title = title;
                        await db.SaveChangesAsync(ct);
                    }
                }
                """),
            Endpoint("Users", "GetUser", "MapGet"),
            ("backend/Features/Users/GetUser/GetUser.cs", """
                using System.Threading;
                using System.Threading.Tasks;
                using Microsoft.EntityFrameworkCore;
                using TemplateApp.Features.Users.Domain;
                using TemplateApp.Infrastructure.Persistence;
                namespace TemplateApp.Features.Users.GetUser;
                public sealed class GetUser(AppDbContext db)
                {
                    public Task<UserProfile?> ExecuteAsync(string authId, CancellationToken ct) =>
                        db.UserProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.AuthId == authId, ct);
                }
                """));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReadSlicesThatWriteOrLoadEntitiesAreRejected()
    {
        var diagnostics = await AnalyzeAsync(
            Endpoint("Tasks", "GetTask", "MapGet"),
            ("backend/Features/Tasks/GetTask/GetTask.cs", """
                using System;
                using System.Collections.Generic;
                using System.Linq;
                using System.Threading;
                using System.Threading.Tasks;
                using Microsoft.EntityFrameworkCore;
                using TemplateApp.Features.Tasks.Domain;
                using TemplateApp.Infrastructure.Persistence;
                namespace TemplateApp.Features.Tasks.GetTask;
                public sealed class GetTask(AppDbContext db)
                {
                    public async Task<TaskItem?> ExecuteAsync(Guid id, CancellationToken ct)
                    {
                        TaskItem? item = await db.TaskItems.SingleOrDefaultAsync(t => t.Id == id, ct);
                        TaskItem? found = await db.TaskItems.FindAsync(new object[] { id }, ct);
                        List<TaskItem> all = db.TaskItems.Where(t => t.OwnerId == "x").ToList();
                        db.TaskItems.Add(new TaskItem());
                        await db.TaskItems.Where(t => t.Id == id).ExecuteDeleteAsync(ct);
                        await db.SaveChangesAsync(ct);
                        return item ?? found ?? all.FirstOrDefault();
                    }
                }
                """),
            Endpoint("Users", "GetUser", "MapGet"),
            ("backend/Features/Users/GetUser/GetUser.cs", """
                using System.Threading;
                using System.Threading.Tasks;
                using TemplateApp.Infrastructure.Persistence;
                namespace TemplateApp.Features.Users.GetUser;
                public sealed class GetUser(AppDbContext db)
                {
                    public Task<int> ExecuteAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
                }
                """));

        Assert.Equal(
            new[]
            {
                ("backend/Features/Tasks/GetTask/GetTask.cs", 14, "carga la entidad 'TaskItem' con SingleOrDefaultAsync"),
                ("backend/Features/Tasks/GetTask/GetTask.cs", 15, "carga la entidad 'TaskItem' con FindAsync"),
                ("backend/Features/Tasks/GetTask/GetTask.cs", 16, "carga la entidad 'TaskItem' con ToList"),
                ("backend/Features/Tasks/GetTask/GetTask.cs", 17, "usa Add"),
                ("backend/Features/Tasks/GetTask/GetTask.cs", 18, "usa ExecuteDeleteAsync"),
                ("backend/Features/Tasks/GetTask/GetTask.cs", 19, "usa SaveChangesAsync"),
                ("backend/Features/Users/GetUser/GetUser.cs", 7, "usa SaveChangesAsync"),
            },
            Findings(diagnostics));
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(params (string Path, string Source)[] files) =>
        AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>(files) { ("backend/Model.cs", Model) },
            AnalyzerHost.Only<ReadSliceAnalyzer>());

    private static (string Path, int Line, string Kind)[] Findings(ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics
            .Where(diagnostic => diagnostic.Id == "DUNE0019")
            .Select(diagnostic => (
                diagnostic.Location.SourceTree!.FilePath,
                diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1,
                Kind(diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture))))
            .OrderBy(finding => finding.FilePath, System.StringComparer.Ordinal)
            .ThenBy(finding => finding.Item2)
            .ToArray();

    private static string Kind(string message)
    {
        int start = message.IndexOf(" y ", System.StringComparison.Ordinal) + 3;
        int end = message.IndexOf(':', start);
        return message.Substring(start, end - start);
    }
}
