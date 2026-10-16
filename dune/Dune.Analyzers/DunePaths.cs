using System;

namespace Dune.Analyzers;

public static class DunePaths
{
    private const string BackendMarker = "backend/";

    public static string Normalize(string? path) =>
        (path ?? string.Empty).Replace('\\', '/');

    public static bool ContainsOrdinal(string path, string value) =>
        path.IndexOf(value, StringComparison.Ordinal) >= 0;

    public static bool IsBackendFile(string normalizedPath) =>
        ContainsOrdinal(normalizedPath, BackendMarker);

    public static bool IsMigration(string normalizedPath) =>
        ContainsOrdinal(normalizedPath, "/Migrations/")
        || normalizedPath.EndsWith(".Designer.cs", StringComparison.Ordinal)
        || ContainsOrdinal(normalizedPath, "ModelSnapshot");

    public static bool IsTestsFile(string normalizedPath) =>
        ContainsOrdinal(normalizedPath, "/Tests/");

    public static bool IsInfrastructureFile(string normalizedPath) =>
        ContainsOrdinal(normalizedPath, "backend/Infrastructure/");

    public static bool IsDomainFile(string normalizedPath) =>
        ContainsOrdinal(normalizedPath, "/Domain/");

    public static bool TryGetFeature(string normalizedPath, out string feature)
    {
        feature = string.Empty;
        var marker = normalizedPath.IndexOf("backend/Features/", StringComparison.Ordinal);
        if (marker < 0)
        {
            return false;
        }

        var rest = normalizedPath.Substring(marker + "backend/Features/".Length);
        var slash = rest.IndexOf('/');
        if (slash <= 0)
        {
            return false;
        }

        feature = rest.Substring(0, slash);
        return true;
    }

    public static bool IsEndpointFile(string normalizedPath, string fileName)
    {
        if (!ContainsOrdinal(normalizedPath, "/Features/"))
        {
            return false;
        }

        return fileName.IndexOf("Endpoint", StringComparison.Ordinal) >= 0;
    }

    public static string? ExpectedNamespace(string normalizedPath)
    {
        var marker = normalizedPath.IndexOf(BackendMarker, StringComparison.Ordinal);
        if (marker < 0)
        {
            return null;
        }

        var relative = normalizedPath.Substring(marker + BackendMarker.Length);
        if (!relative.EndsWith(".cs", StringComparison.Ordinal))
        {
            return null;
        }

        relative = relative.Substring(0, relative.Length - ".cs".Length);
        var slash = relative.LastIndexOf('/');
        if (slash < 0)
        {
            return "TemplateApp";
        }

        return "TemplateApp." + relative.Substring(0, slash).Replace('/', '.');
    }
}
