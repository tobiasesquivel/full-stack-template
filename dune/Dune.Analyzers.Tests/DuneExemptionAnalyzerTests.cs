using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Dune.Analyzers.Tests;

public sealed class DuneExemptionAnalyzerTests
{
    [Fact]
    public async Task AnExemptionWithACheckAndAReason_IsAccepted()
    {
        var diagnostics = await AnalyzeAsync("""
            using TemplateApp.Shared.Architecture;
            namespace TemplateApp.Features.Auth.IssueCsrfToken;
            [DuneExemption(DuneCheck.MediatorEndpoint, "Solo escribe la cookie de antiforgery.")]
            internal static class IssueCsrfTokenEndpoint
            {
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("[DuneExemption(DuneCheck.MediatorEndpoint, \"\")]", "exime MediatorEndpoint sin decir el motivo")]
    [InlineData("[DuneExemption(DuneCheck.MediatorEndpoint, \"   \")]", "exime MediatorEndpoint sin decir el motivo")]
    [InlineData("[DuneExemption(DuneCheck.MediatorEndpoint, null!)]", "exime MediatorEndpoint sin decir el motivo")]
    [InlineData("[DuneExemption((DuneCheck)99, \"Un chequeo que no existe.\")]", "nombra un chequeo que no está en DuneCheck")]
    public async Task AnExemptionWithoutReason_OrWithAnUnknownCheck_IsRejected(string attribute, string expected)
    {
        var diagnostics = await AnalyzeAsync($$"""
            using TemplateApp.Shared.Architecture;
            namespace TemplateApp.Features.Auth.IssueCsrfToken;
            {{attribute}}
            internal static class IssueCsrfTokenEndpoint
            {
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("DUNE0033", diagnostic.Id);
        Assert.Equal(3, diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1);
        Assert.Contains("'IssueCsrfTokenEndpoint' " + expected, diagnostic.GetMessage(), System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAttributeWithTheSameNameInAnotherNamespace_IsNotAnExemption()
    {
        var diagnostics = await AnalyzeAsync("""
            namespace Elsewhere
            {
                public enum DuneCheck { MediatorEndpoint }
                [System.AttributeUsage(System.AttributeTargets.Class)]
                public sealed class DuneExemptionAttribute : System.Attribute
                {
                    public DuneExemptionAttribute(DuneCheck check, string reason) { }
                }
                [DuneExemption(DuneCheck.MediatorEndpoint, "")]
                internal static class Probe
                {
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source) =>
        AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                (ExemptionFixture.Path, ExemptionFixture.Source),
                ("backend/Features/Auth/IssueCsrfToken/IssueCsrfTokenEndpoint.cs", source),
            },
            AnalyzerHost.Only<DuneExemptionAnalyzer>());
}
