using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Dune.Analyzers.Tests;

public sealed class MeterConstructionBanTests
{
    [Fact]
    public async Task EveryMeterConstructorIsBannedInTheRealList()
    {
        var diagnostics = await BannedApiHost.AnalyzeAsync(
            RealBannedSymbols(),
            new List<(string, string)>
            {
                ("backend/Features/Tasks/Telemetry/TaskMetrics.cs", """
                    using System.Collections.Generic;
                    using System.Diagnostics.Metrics;
                    namespace TemplateApp.Features.Tasks.Telemetry;
                    public sealed class TaskMetrics
                    {
                        private readonly Meter _byName = new("TemplateApp.Tasks");
                        private readonly Meter _byVersion = new("TemplateApp.Tasks", "1.0.0");
                        private readonly Meter _byScope = new("TemplateApp.Tasks", "1.0.0", new List<KeyValuePair<string, object?>>(), null);
                        private readonly Meter _byOptions = new(new MeterOptions("TemplateApp.Tasks"));
                    }
                    """),
            });

        var lines = diagnostics
            .Where(diagnostic => diagnostic.Id == "RS0030")
            .Select(diagnostic => diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1)
            .Distinct()
            .OrderBy(line => line)
            .ToArray();
        Assert.Equal(new[] { 6, 7, 8, 9 }, lines);
    }

    [Fact]
    public async Task AMeterFromTheHostFactoryStaysAllowed()
    {
        var diagnostics = await BannedApiHost.AnalyzeAsync(
            RealBannedSymbols(),
            new List<(string, string)>
            {
                ("backend/Features/Tasks/Telemetry/TaskMetrics.cs", """
                    using System.Diagnostics.Metrics;
                    namespace TemplateApp.Features.Tasks.Telemetry;
                    public sealed class TaskMetrics
                    {
                        private readonly Counter<long> _created;

                        public TaskMetrics(IMeterFactory meterFactory)
                        {
                            Meter meter = meterFactory.Create("TemplateApp.Tasks");
                            _created = meter.CreateCounter<long>("templateapp.tasks.created", "{task}");
                        }

                        public void TaskCreated() => _created.Add(1);
                    }
                    """),
            });

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "RS0030");
    }

    private static string RealBannedSymbols()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "dune", "BannedSymbols.txt");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
        }

        throw new InvalidOperationException("dune/BannedSymbols.txt not found above the test output.");
    }
}
