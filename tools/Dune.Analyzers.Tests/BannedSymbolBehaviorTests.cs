using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Dune.Analyzers.Tests;

// Banned symbols are semantic: the check keys on the resolved symbol, so an
// alias or a fully qualified name hits the same entry. Tests declare the
// entry list in code because the repo-level BannedSymbols.txt is resolved by
// MSBuild at compile time, not by an in-memory test compilation.
public sealed class BannedSymbolBehaviorTests
{
    private const string BannedSymbols = """
        P:System.DateTime.Now;Do not use local clock time.
        P:System.DateTime.Today;Do not use local clock date.
        P:System.DateTimeOffset.Now;Do not use local clock time.
        M:System.Threading.Tasks.Task.Wait();Do not block on tasks.
        P:System.Threading.Tasks.Task`1.Result;Do not block on tasks.
        """;

    [Fact]
    public async Task AliasAndQualifiedClockReadsHitTheSameBan()
    {
        var diagnostics = await BannedApiHost.AnalyzeAsync(
            BannedSymbols,
            new List<(string, string)>
            {
                ("backend/Features/Tasks/Probe.cs", """
                    using IO = System;
                    namespace TemplateApp.Features.Tasks;
                    public sealed class Probe
                    {
                        public object Read()
                        {
                            var a = IO::DateTime.Now;
                            var b = System.DateTime.Today;
                            return (a, b);
                        }
                    }
                    """),
            });

        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == "RS0030"));
    }

    [Fact]
    public async Task BlockingWaitAndResultAreBanned()
    {
        var diagnostics = await BannedApiHost.AnalyzeAsync(
            BannedSymbols,
            new List<(string, string)>
            {
                ("backend/Features/Tasks/Blocking.cs", """
                    using System.Threading.Tasks;
                    namespace TemplateApp.Features.Tasks;
                    public sealed class Blocking
                    {
                        public int Read(Task<int> task)
                        {
                            task.Wait();
                            return task.Result;
                        }
                    }
                    """),
            });

        Assert.True(
            diagnostics.Count(diagnostic => diagnostic.Id == "RS0030") >= 2,
            "Wait() and Result must both report RS0030.");
    }

    [Fact]
    public async Task RawSqlStaysBannedWhileFormattableFromSqlStaysAllowed()
    {
        const string entries = BannedSymbols + """

            M:Microsoft.EntityFrameworkCore.RelationalQueryableExtensions.FromSqlRaw``1(Microsoft.EntityFrameworkCore.DbSet{``0},System.String,System.Object[]);Do not interpolate raw SQL.
            """;

        var banned = await BannedApiHost.AnalyzeAsync(
            entries,
            new List<(string, string)>
            {
                ("backend/Features/Tasks/RawSql.cs", """
                    using Microsoft.EntityFrameworkCore;
                    namespace TemplateApp.Features.Tasks;
                    public sealed class RawSql
                    {
                        public object Query(DbSet<string> items, string title)
                            => items.FromSqlRaw("SELECT * FROM x WHERE title = {0}", title);
                    }
                    """),
            });

        Assert.Contains(banned, diagnostic => diagnostic.Id == "RS0030");
    }

    [Fact]
    public async Task AwaitedTaskAndTimeProviderStayAllowed()
    {
        var diagnostics = await BannedApiHost.AnalyzeAsync(
            BannedSymbols,
            new List<(string, string)>
            {
                ("backend/Features/Tasks/Clock.cs", """
                    using System;
                    using System.Threading.Tasks;
                    namespace TemplateApp.Features.Tasks;
                    public sealed class Clock
                    {
                        private readonly TimeProvider _clock;
                        public Clock(TimeProvider clock) => _clock = clock;
                        public async Task<DateTimeOffset> ReadAsync(Task<int> task)
                        {
                            _ = await task.ConfigureAwait(false);
                            return _clock.GetUtcNow();
                        }
                    }
                    """),
            });

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "RS0030");
    }
}
