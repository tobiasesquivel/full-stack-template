using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Dune.Analyzers.Tests;

internal static class TestReferences
{
    public static List<MetadataReference> All()
    {
        _ = typeof(Microsoft.EntityFrameworkCore.DbContext).Assembly;
        _ = typeof(Microsoft.EntityFrameworkCore.RelationalQueryableExtensions).Assembly;
        _ = typeof(Microsoft.AspNetCore.Identity.IdentityUser).Assembly;
        _ = typeof(Microsoft.AspNetCore.Identity.UserManager<>).Assembly;
        _ = typeof(System.Linq.Queryable).Assembly;

        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        var paths = (tpa ?? string.Empty)
            .Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var locations = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(
            Path.GetDirectoryName(typeof(TestReferences).Assembly.Location)!,
            "*.dll"))
        {
            locations.Add(file);
        }

        return locations
            .GroupBy(location => Path.GetFileName(location), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(location =>
                Path.GetDirectoryName(location)!.StartsWith(
                    Path.GetDirectoryName(typeof(TestReferences).Assembly.Location)!,
                    StringComparison.OrdinalIgnoreCase) ? 1 : 0).First())
            .Select(location => MetadataReference.CreateFromFile(location))
            .Cast<MetadataReference>()
            .ToList();
    }

    public static void ThrowOnFixtureErrors(CSharpCompilation compilation)
    {
        var errors = compilation.GetDiagnostics()
            .Where(diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error
                && !diagnostic.Id.StartsWith("DUNE", StringComparison.Ordinal)
                && !diagnostic.Id.StartsWith("RS", StringComparison.Ordinal))
            .ToList();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Fixture compilation has errors: "
                + string.Join("; ", errors.Select(error =>
                    error.Id + " " + error.GetMessage())));
        }
    }
}
