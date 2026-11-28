using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Dune.Analyzers.Tests;

public sealed class FeaturePermissionsAnalyzerTests
{
    private const string Contract = """
        namespace TemplateApp.Infrastructure.Access;
        public interface IFeaturePermissions
        {
            string[] Grants { get; }
        }
        """;

    private const string DeclarationPath = "backend/Features/Invoices/Access/InvoicesPermissions.cs";

    private const string Declaration = """
        using TemplateApp.Infrastructure.Access;
        namespace TemplateApp.Features.Invoices.Access;
        internal sealed class InvoicesPermissions : IFeaturePermissions
        {
            public const string Read = "Permissions.Invoices.Read";
            public string[] Grants { get; } = [Read];
        }
        """;

    private const string Handler = """
        using TemplateApp.Features.Invoices.Access;
        namespace TemplateApp.Features.Invoices.GetInvoice;
        internal static class GetInvoiceQueryHandler
        {
            public static string Needed() => InvoicesPermissions.Read;
        }
        """;

    [Fact]
    public async Task ADeclarationInItsFile_AndAFeatureThatUsesItsConstant_AreAccepted()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Infrastructure/Access/IFeaturePermissions.cs", Contract),
            (DeclarationPath, Declaration),
            ("backend/Features/Invoices/GetInvoice/GetInvoiceQueryHandler.cs", Handler));

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("backend/Features/Auth/Access/InvoicePermissionNames.cs", "TemplateApp.Features.Auth.Access")]
    [InlineData("backend/Features/Invoices/GetInvoice/GetInvoiceQueryHandler.cs", "TemplateApp.Features.Invoices.GetInvoice")]
    [InlineData("backend/Features/Invoices/InvoicesPermissions.cs", "TemplateApp.Features.Invoices")]
    [InlineData("backend/Infrastructure/Access/Grants.cs", "TemplateApp.Infrastructure.Access")]
    public async Task APermissionLiteral_OutsideTheDeclarationOfItsFeature_IsRejected(string path, string ns)
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Infrastructure/Access/IFeaturePermissions.cs", Contract),
            (DeclarationPath, Declaration),
            (path, $$"""
                namespace {{ns}};
                internal static class Copy
                {
                    public const string Read = "Permissions.Invoices.Read";
                }
                """));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("DUNE0035", diagnostic.Id);
        Assert.Contains("'Permissions.Invoices.Read' es un permiso de Invoices y solo se escribe en backend/Features/Invoices/Access/InvoicesPermissions.cs", diagnostic.GetMessage(), System.StringComparison.Ordinal);
        Assert.EndsWith(path, diagnostic.Location.SourceTree!.FilePath, System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADeclarationThatWritesAnotherFeaturesPermission_IsRejected()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Infrastructure/Access/IFeaturePermissions.cs", Contract),
            (DeclarationPath, """
                using TemplateApp.Infrastructure.Access;
                namespace TemplateApp.Features.Invoices.Access;
                internal sealed class InvoicesPermissions : IFeaturePermissions
                {
                    public string[] Grants { get; } = ["Permissions.Users.Manage"];
                }
                """));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("'Permissions.Users.Manage' es un permiso de Users", diagnostic.GetMessage(), System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DeclarationPath, "internal sealed class InvoicePermissions : IFeaturePermissions { public string[] Grants => []; }", "tiene que llamarse InvoicesPermissions")]
    [InlineData(DeclarationPath, "internal class InvoicesPermissions : IFeaturePermissions { public string[] Grants => []; }", "no es una clase sealed")]
    [InlineData(DeclarationPath, "internal abstract class InvoicesPermissions : IFeaturePermissions { public string[] Grants => []; }", "no es una clase sealed")]
    [InlineData(DeclarationPath, "internal sealed class InvoicesPermissions : IFeaturePermissions { public InvoicesPermissions(int size) { } public string[] Grants => []; }", "no tiene un constructor público sin parámetros")]
    [InlineData(DeclarationPath, "internal sealed class InvoicesPermissions<T> : IFeaturePermissions { public string[] Grants => []; }", "no es la clase <Feature>Permissions de primer nivel")]
    [InlineData(DeclarationPath, "file sealed class InvoicesPermissions : IFeaturePermissions { public string[] Grants => []; }", "no es la clase <Feature>Permissions de primer nivel")]
    [InlineData(DeclarationPath, "internal static class Holder { internal sealed class InvoicesPermissions : IFeaturePermissions { public string[] Grants => []; } }", "no es la clase <Feature>Permissions de primer nivel")]
    [InlineData("backend/Features/Invoices/InvoicesPermissions.cs", "internal sealed class InvoicesPermissions : IFeaturePermissions { public string[] Grants => []; }", "no es la clase <Feature>Permissions de primer nivel")]
    [InlineData("backend/Infrastructure/Access/InvoicesPermissions.cs", "internal sealed class InvoicesPermissions : IFeaturePermissions { public string[] Grants => []; }", "no es la clase <Feature>Permissions de primer nivel")]
    public async Task ADeclarationTheHostCannotUse_IsRejected(string path, string declaration, string expected)
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Infrastructure/Access/IFeaturePermissions.cs", Contract),
            (path, $$"""
                using TemplateApp.Infrastructure.Access;
                namespace TemplateApp.Features.Invoices.Access;
                {{declaration}}
                """));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("DUNE0035", diagnostic.Id);
        Assert.Contains(expected, diagnostic.GetMessage(), System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheTestsProject_IsLeftOut()
    {
        var diagnostics = await AnalyzeProjectAsync(
            "backend/Tests/",
            ("backend/Infrastructure/Access/IFeaturePermissions.cs", Contract),
            ("backend/Tests/Authorization/Expected.cs", """
                namespace TemplateApp.Tests.Authorization;
                internal static class Expected
                {
                    public const string Read = "Permissions.Invoices.Read";
                }
                """),
            ("backend/Tests/Authorization/ProbePermissions.cs", """
                using TemplateApp.Infrastructure.Access;
                namespace TemplateApp.Tests.Authorization;
                internal class ProbePermissions : IFeaturePermissions
                {
                    public string[] Grants => [];
                }
                """));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AHost_IsChecked_EvenForAFileThatLooksLikeATest()
    {
        var diagnostics = await AnalyzeProjectAsync(
            "backend/Hosts/Monolith/",
            ("backend/Infrastructure/Access/IFeaturePermissions.cs", Contract),
            ("backend/Features/Auth/Access/InvoiceNamesTests.cs", """
                namespace TemplateApp.Features.Auth.Access;
                internal static class InvoiceNamesTests
                {
                    public const string Read = "Permissions.Invoices.Read";
                }
                """));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("DUNE0035", diagnostic.Id);
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeProjectAsync(string projectPath, params (string Path, string Source)[] files) =>
        AnalyzerHost.AnalyzeProjectAsync(
            "TemplateApp",
            ImmutableDictionary<string, string>.Empty.Add(HostBoundaryAnalyzer.ProjectPathOption, projectPath),
            new List<(string, string)>(files),
            System.Array.Empty<MetadataReference>(),
            AnalyzerHost.Only<FeaturePermissionsAnalyzer>());

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(params (string Path, string Source)[] files) =>
        AnalyzerHost.AnalyzeAsync(new List<(string, string)>(files), AnalyzerHost.Only<FeaturePermissionsAnalyzer>());
}
