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
        Assert.Contains("'" + name + "' implementa IFeature fuera de", diagnostic.GetMessage(), System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFeatureWithoutTheContract_ReportsThatTheRuleIsBlind()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Invoices/CreateInvoice/CreateInvoiceEndpoint.cs", Slice));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("no se puede revisar", diagnostic.GetMessage(), System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestsAndMigrations_AreLeftOut()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Infrastructure/Hosting/IFeature.cs", Contract),
            ("backend/Features/Invoices/CreateInvoice/CreateInvoiceTests.cs", """
                namespace TemplateApp.Features.Invoices.CreateInvoice;
                public sealed class CreateInvoiceTests
                {
                }
                """),
            ("backend/Features/Invoices/Persistence/Migrations/20261127000000_Init.cs", """
                namespace TemplateApp.Features.Invoices.Persistence.Migrations;
                internal sealed class Init
                {
                }
                """),
            ("backend/Tests/Hosting/ProbeFeature.cs", """
                using TemplateApp.Infrastructure.Hosting;
                namespace TemplateApp.Tests.Hosting;
                internal sealed class ProbeFeature : IFeature
                {
                    public void AddServices()
                    {
                    }
                }
                """));

        Assert.Empty(diagnostics);
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(params (string Path, string Source)[] files) =>
        AnalyzerHost.AnalyzeAsync(new List<(string, string)>(files), AnalyzerHost.Only<FeatureDiscoveryAnalyzer>());
}
