using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Dune.Analyzers.Tests;

public sealed class FeatureDiscoveryAnalyzerTests
{
    private const string Contract = """
        namespace TemplateApp.Infrastructure.Hosting;
        public interface IFeature
        {
            void AddServices();
        }
        """;

    private const string Slice = """
        namespace TemplateApp.Features.Invoices.CreateInvoice;
        internal static class CreateInvoiceEndpoint
        {
        }
        """;

    [Fact]
    public async Task AFeatureThatImplementsIFeature_IsAccepted()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Infrastructure/Hosting/IFeature.cs", Contract),
            ("backend/Features/Invoices/CreateInvoice/CreateInvoiceEndpoint.cs", Slice),
            ("backend/Features/Invoices/InvoicesFeature.cs", """
                using TemplateApp.Infrastructure.Hosting;
                namespace TemplateApp.Features.Invoices;
                internal sealed class InvoicesFeature : IFeature
                {
                    public const string Name = "invoices";
                    public void AddServices()
                    {
                    }
                }
                """));

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("internal static class InvoicesFeature { }", "es una clase estática, abstracta o no es una clase")]
    [InlineData("internal abstract class InvoicesFeature : IFeature { public void AddServices() { } }", "es una clase estática, abstracta o no es una clase")]
    [InlineData("internal class InvoicesFeature : IFeature { public void AddServices() { } }", "no es sealed")]
    [InlineData("internal sealed class InvoicesFeature { public void AddServices() { } }", "no implementa IFeature")]
    [InlineData("internal sealed class InvoicesFeature : IFeature { private InvoicesFeature() { } public void AddServices() { } }", "no tiene un constructor público sin parámetros")]
    [InlineData("internal sealed class InvoicesFeature : IFeature { public InvoicesFeature(int size) { } public void AddServices() { } }", "no tiene un constructor público sin parámetros")]
    public async Task AFeatureTheHostCannotDiscover_IsRejected(string declaration, string expected)
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Infrastructure/Hosting/IFeature.cs", Contract),
            ("backend/Features/Invoices/CreateInvoice/CreateInvoiceEndpoint.cs", Slice),
            ("backend/Features/Invoices/InvoicesFeature.cs", $$"""
                using TemplateApp.Infrastructure.Hosting;
                namespace TemplateApp.Features.Invoices;
                {{declaration}}
                """));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("DUNE0034", diagnostic.Id);
        Assert.Contains(expected, diagnostic.GetMessage(), System.StringComparison.Ordinal);
        Assert.EndsWith("InvoicesFeature.cs", diagnostic.Location.SourceTree!.FilePath, System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFeatureClassFromAnotherAssembly_IsRejected()
    {
        var library = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "Other",
            new[]
            {
                Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(Contract),
                Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("""
                    using TemplateApp.Infrastructure.Hosting;
                    namespace TemplateApp.Features.Invoices;
                    public sealed class InvoicesFeature : IFeature
                    {
                        public void AddServices()
                        {
                        }
                    }
                    """),
            },
            TestReferences.All(),
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var diagnostics = await AnalyzerHost.AnalyzeProjectAsync(
            "TemplateApp",
            ImmutableDictionary<string, string>.Empty,
            new List<(string, string)> { ("backend/Features/Invoices/CreateInvoice/CreateInvoiceEndpoint.cs", Slice) },
            new[] { library.ToMetadataReference() },
            AnalyzerHost.Only<FeatureDiscoveryAnalyzer>());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("'InvoicesFeature' viene de otro ensamblado (Other)", diagnostic.GetMessage(), System.StringComparison.Ordinal);
        Assert.EndsWith("CreateInvoiceEndpoint.cs", diagnostic.Location.SourceTree!.FilePath, System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFeatureFolderWithoutItsFeatureClass_IsRejected()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Infrastructure/Hosting/IFeature.cs", Contract),
            ("backend/Features/Invoices/CreateInvoice/CreateInvoiceEndpoint.cs", Slice));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("'InvoicesFeature' falta: la feature Invoices compila en este host", diagnostic.GetMessage(), System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("backend/Features/Invoices/CreateInvoice/BillingFeature.cs", "TemplateApp.Features.Invoices.CreateInvoice", "BillingFeature")]
    [InlineData("backend/Features/Invoices/Billing.cs", "TemplateApp.Features.Invoices", "Billing")]
    [InlineData("backend/Infrastructure/Hosting/PlatformFeature.cs", "TemplateApp.Infrastructure.Hosting", "PlatformFeature")]
    public async Task AnIFeatureOutsideItsFeatureFile_IsRejected(string path, string ns, string name)
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Infrastructure/Hosting/IFeature.cs", Contract),
            ("backend/Features/Invoices/CreateInvoice/CreateInvoiceEndpoint.cs", Slice),
            ("backend/Features/Invoices/InvoicesFeature.cs", """
                using TemplateApp.Infrastructure.Hosting;
                namespace TemplateApp.Features.Invoices;
                internal sealed class InvoicesFeature : IFeature
                {
                    public void AddServices()
                    {
                    }
                }
                """),
            (path, $$"""
                using TemplateApp.Infrastructure.Hosting;
                namespace {{ns}};
                internal sealed class {{name}} : IFeature
                {
                    public void AddServices()
                    {
                    }
                }
                """));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("'" + name + "' implementa IFeature y no es la clase", diagnostic.GetMessage(), System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("file sealed class InvoicesFeature<T> : IFeature { public void AddServices() { } }")]
    [InlineData("internal sealed class InvoicesFeature<T> : IFeature { public void AddServices() { } }")]
    [InlineData("file sealed class Helper : IFeature { public void AddServices() { } }")]
    public async Task AGenericOrFileLocalIFeature_NextToTheFeatureClass_IsRejected(string extra)
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Infrastructure/Hosting/IFeature.cs", Contract),
            ("backend/Features/Invoices/CreateInvoice/CreateInvoiceEndpoint.cs", Slice),
            ("backend/Features/Invoices/InvoicesFeature.cs", $$"""
                using TemplateApp.Infrastructure.Hosting;
                namespace TemplateApp.Features.Invoices;
                internal sealed class InvoicesFeature : IFeature
                {
                    public void AddServices()
                    {
                    }
                }
                {{extra}}
                """));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("implementa IFeature y no es la clase", diagnostic.GetMessage(), System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFeatureNamedTests_IsStillAFeature()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Infrastructure/Hosting/IFeature.cs", Contract),
            ("backend/Features/Tests/RunChecks/Check.cs", """
                namespace TemplateApp.Features.Tests.RunChecks;
                internal static class Check
                {
                }
                """));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("'TestsFeature' falta", diagnostic.GetMessage(), System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task AGenericIFeatureInGeneratedCode_IsRejected()
    {
        await AssertGenericProbeIsRejectedAsync("backend/Features/Invoices/Probe.g.cs");
    }

    [Fact]
    public async Task AGenericIFeatureOutsideTheRepo_IsRejected()
    {
        await AssertGenericProbeIsRejectedAsync("C:/build/obj/TemplateApp/Release/Probe.g.cs");
    }

    private static async Task AssertGenericProbeIsRejectedAsync(string probePath)
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Infrastructure/Hosting/IFeature.cs", Contract),
            ("backend/Features/Invoices/CreateInvoice/CreateInvoiceEndpoint.cs", Slice),
            ("backend/Features/Invoices/InvoicesFeature.cs", """
                using TemplateApp.Infrastructure.Hosting;
                namespace TemplateApp.Features.Invoices;
                internal sealed class InvoicesFeature : IFeature
                {
                    public void AddServices()
                    {
                    }
                }
                """),
            (probePath, """
                // <auto-generated/>
                using TemplateApp.Infrastructure.Hosting;
                namespace TemplateApp.Features.Invoices;
                internal sealed class Probe<T> : IFeature
                {
                    public void AddServices()
                    {
                    }
                }
                """));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("'Probe' implementa IFeature y no es la clase", diagnostic.GetMessage(), System.StringComparison.Ordinal);
        Assert.EndsWith("Probe.g.cs", diagnostic.Location.SourceTree!.FilePath, System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFeatureWithoutTheContract_ReportsThatTheRuleIsBlind()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Invoices/CreateInvoice/CreateInvoiceEndpoint.cs", Slice));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("no se puede revisar", diagnostic.GetMessage(), System.StringComparison.Ordinal);
    }

    private const string GenericProbe = """
        using TemplateApp.Infrastructure.Hosting;
        namespace TemplateApp.Features.Invoices;
        internal sealed class Probe<T> : IFeature
        {
            public void AddServices()
            {
            }
        }
        """;

    private const string ValidFeature = """
        using TemplateApp.Infrastructure.Hosting;
        namespace TemplateApp.Features.Invoices;
        internal sealed class InvoicesFeature : IFeature
        {
            public void AddServices()
            {
            }
        }
        """;

    [Theory]
    [InlineData("Migrations")]
    [InlineData("ModelSnapshots")]
    [InlineData("Designer")]
    public async Task AFeatureWhoseNameLooksLikeAMigration_StillNeedsItsFeatureClass(string feature)
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Infrastructure/Hosting/IFeature.cs", Contract),
            ("backend/Features/" + feature + "/Run/RunEndpoint.cs", $$"""
                namespace TemplateApp.Features.{{feature}}.Run;
                internal static class RunEndpoint
                {
                }
                """));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("'" + feature + "Feature' falta", diagnostic.GetMessage(), System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFeatureFolderWithOnlyMigrations_StillNeedsItsFeatureClass()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Infrastructure/Hosting/IFeature.cs", Contract),
            ("backend/Features/Invoices/Persistence/Migrations/20261127000000_Init.cs", """
                namespace TemplateApp.Features.Invoices.Persistence.Migrations;
                internal sealed class Init
                {
                }
                """));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("'InvoicesFeature' falta", diagnostic.GetMessage(), System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheTestsProject_IsLeftOut()
    {
        var diagnostics = await AnalyzeProjectAsync(
            "backend/Tests/",
            ("backend/Infrastructure/Hosting/IFeature.cs", Contract),
            ("backend/Features/Invoices/CreateInvoice/CreateInvoiceTests.cs", """
                namespace TemplateApp.Features.Invoices.CreateInvoice;
                public sealed class CreateInvoiceTests
                {
                }
                """),
            ("backend/Tests/Hosting/InvoicesFeature.cs", """
                using TemplateApp.Infrastructure.Hosting;
                namespace TemplateApp.Tests.Hosting;
                public sealed class InvoicesFeature : IFeature
                {
                    public void AddServices()
                    {
                    }
                }
                """));

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("C:/build/obj/TemplateApp/Release/ProbeTests.cs")]
    [InlineData("backend/Features/Invoices/ProbeTests.cs")]
    [InlineData("backend/Tests/Hosting/Probe.cs")]
    public async Task AGenericIFeatureInAHost_IsRejected_EvenIfItsFileLooksLikeATest(string probePath)
    {
        var diagnostics = await AnalyzeProjectAsync(
            "backend/Hosts/Monolith/",
            ("backend/Infrastructure/Hosting/IFeature.cs", Contract),
            ("backend/Features/Invoices/CreateInvoice/CreateInvoiceEndpoint.cs", Slice),
            ("backend/Features/Invoices/InvoicesFeature.cs", ValidFeature),
            (probePath, GenericProbe));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("'Probe' implementa IFeature y no es la clase", diagnostic.GetMessage(), System.StringComparison.Ordinal);
        Assert.EndsWith(probePath, diagnostic.Location.SourceTree!.FilePath, System.StringComparison.Ordinal);
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeProjectAsync(string projectPath, params (string Path, string Source)[] files) =>
        AnalyzerHost.AnalyzeProjectAsync(
            "TemplateApp",
            ImmutableDictionary<string, string>.Empty.Add(HostBoundaryAnalyzer.ProjectPathOption, projectPath),
            new List<(string, string)>(files),
            System.Array.Empty<MetadataReference>(),
            AnalyzerHost.Only<FeatureDiscoveryAnalyzer>());

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(params (string Path, string Source)[] files) =>
        AnalyzerHost.AnalyzeAsync(new List<(string, string)>(files), AnalyzerHost.Only<FeatureDiscoveryAnalyzer>());
}
