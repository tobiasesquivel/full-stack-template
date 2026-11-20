using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Dune.Analyzers.Tests;

public sealed class AppendOnlyAnalyzerTests
{
    private const string Model = """
        using Microsoft.EntityFrameworkCore;
        namespace TemplateApp.Shared.Domain
        {
            public interface IAppendOnly { }
        }
        namespace TemplateApp.Features.Users.Domain
        {
            public sealed class AccountClosureHistoryEntry : TemplateApp.Shared.Domain.IAppendOnly
            {
                public long Id { get; init; }
                public System.Guid ClosureId { get; init; }
                public string Kind { get; init; } = "";
            }
            public sealed class UserProfile
            {
                public string AuthId { get; set; } = "";
                public string DisplayName { get; set; } = "";
            }
        }
        namespace TemplateApp.Features.Users.Persistence
        {
            public sealed class UsersDbContext : DbContext
            {
                public DbSet<TemplateApp.Features.Users.Domain.AccountClosureHistoryEntry> AccountClosureHistory => Set<TemplateApp.Features.Users.Domain.AccountClosureHistoryEntry>();
                public DbSet<TemplateApp.Features.Users.Domain.UserProfile> UserProfiles => Set<TemplateApp.Features.Users.Domain.UserProfile>();
            }
        }
        """;

    [Fact]
    public async Task AddingAndReadingAppendOnlyRows_AndRewritingOtherEntities_AreAccepted()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Users/AccountClosureHistory.cs", """
                using System;
                using System.Linq;
                using System.Threading;
                using System.Threading.Tasks;
                using Microsoft.EntityFrameworkCore;
                using TemplateApp.Features.Users.Domain;
                using TemplateApp.Features.Users.Persistence;
                namespace TemplateApp.Features.Users;
                public sealed class AccountClosureHistory(UsersDbContext db)
                {
                    public Task<int> RecordAsync(Guid closureId, CancellationToken ct)
                    {
                        db.AccountClosureHistory.Add(new AccountClosureHistoryEntry { ClosureId = closureId, Kind = "requested" });
                        db.AddRange(new AccountClosureHistoryEntry { ClosureId = closureId, Kind = "cancelled" });
                        return db.SaveChangesAsync(ct);
                    }

                    public Task<string[]> ReadAsync(Guid closureId, CancellationToken ct) =>
                        db.AccountClosureHistory.Where(e => e.ClosureId == closureId).OrderBy(e => e.Id).Select(e => e.Kind).ToArrayAsync(ct);

                    public async Task<int> RenameAsync(string authId, CancellationToken ct)
                    {
                        var profile = await db.UserProfiles.SingleAsync(p => p.AuthId == authId, ct);
                        db.UserProfiles.Update(profile);
                        db.Remove(profile);
                        return await db.UserProfiles.Where(p => p.AuthId == authId).ExecuteDeleteAsync(ct);
                    }
                }
                """),
            ("backend/Features/Users/AccountClosureHistoryTests.cs", """
                using System.Threading.Tasks;
                using Microsoft.EntityFrameworkCore;
                using TemplateApp.Features.Users.Persistence;
                namespace TemplateApp.Features.Users;
                public sealed class AccountClosureHistoryTests
                {
                    public Task<int> ResetAsync(UsersDbContext db) => db.AccountClosureHistory.ExecuteDeleteAsync();
                }
                """));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task UpdatingOrRemovingAppendOnlyRows_InEveryForm_IsRejected()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Users/RewriteHistory.cs", """
                using System;
                using System.Linq;
                using System.Threading;
                using System.Threading.Tasks;
                using Microsoft.EntityFrameworkCore;
                using TemplateApp.Features.Users.Domain;
                using TemplateApp.Features.Users.Persistence;
                namespace TemplateApp.Features.Users;
                public sealed class RewriteHistory(UsersDbContext db)
                {
                    public async Task ExecuteAsync(Guid closureId, CancellationToken ct)
                    {
                        var rows = await db.AccountClosureHistory.Where(e => e.ClosureId == closureId).ToListAsync(ct);
                        db.AccountClosureHistory.Update(rows[0]);
                        db.AccountClosureHistory.UpdateRange(rows);
                        db.AccountClosureHistory.Remove(rows[0]);
                        db.AccountClosureHistory.RemoveRange(rows);
                        db.Update(rows[0]);
                        db.Remove((object)rows[0]);
                        db.RemoveRange(rows[0], rows[1]);
                        db.AccountClosureHistory.Where(e => e.ClosureId == closureId).ExecuteUpdate(s => s.SetProperty(e => e.Kind, "x"));
                        await db.AccountClosureHistory.Where(e => e.ClosureId == closureId).ExecuteUpdateAsync(s => s.SetProperty(e => e.Kind, "x"), ct);
                        db.AccountClosureHistory.Where(e => e.ClosureId == closureId).ExecuteDelete();
                        await db.AccountClosureHistory.ExecuteDeleteAsync(ct);
                    }
                }
                """),
            ("backend/Features/Users/Domain/MutableHistoryEntry.cs", """
                namespace TemplateApp.Features.Users.Domain;
                public sealed class MutableHistoryEntry : TemplateApp.Shared.Domain.IAppendOnly
                {
                    public long Id { get; init; }
                    public string Kind { get; set; } = "";
                    public string? Detail { get; private set; }
                    public string Label => Kind;
                }
                """));

        Assert.Equal(
            new[]
            {
                ("backend/Features/Users/Domain/MutableHistoryEntry.cs", 5),
                ("backend/Features/Users/Domain/MutableHistoryEntry.cs", 6),
                ("backend/Features/Users/RewriteHistory.cs", 14),
                ("backend/Features/Users/RewriteHistory.cs", 15),
                ("backend/Features/Users/RewriteHistory.cs", 16),
                ("backend/Features/Users/RewriteHistory.cs", 17),
                ("backend/Features/Users/RewriteHistory.cs", 18),
                ("backend/Features/Users/RewriteHistory.cs", 19),
                ("backend/Features/Users/RewriteHistory.cs", 20),
                ("backend/Features/Users/RewriteHistory.cs", 21),
                ("backend/Features/Users/RewriteHistory.cs", 22),
                ("backend/Features/Users/RewriteHistory.cs", 23),
                ("backend/Features/Users/RewriteHistory.cs", 24),
            },
            Locations(diagnostics));
    }

    private static Task<System.Collections.Immutable.ImmutableArray<Diagnostic>> AnalyzeAsync(
        params (string Path, string Source)[] files) =>
        AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>(files) { ("backend/Model.cs", Model) },
            AnalyzerHost.Only<AppendOnlyAnalyzer>());

    private static (string Path, int Line)[] Locations(System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics
            .Where(diagnostic => diagnostic.Id == "DUNE0026")
            .Select(diagnostic => (
                diagnostic.Location.SourceTree!.FilePath,
                diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1))
            .OrderBy(location => location.FilePath, System.StringComparer.Ordinal)
            .ThenBy(location => location.Item2)
            .ToArray();
}
