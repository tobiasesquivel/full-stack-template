using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Dune.Analyzers;

public static class DuneCommentPolicy
{
    public static bool IsExemptPath(string normalizedPath) =>
        DunePaths.ContainsOrdinal(normalizedPath, "backend/Infrastructure/Persistence/Migrations/")
        || IsFeatureMigration(normalizedPath)
        || IsHostGeneratedHandler(normalizedPath);

    private static bool IsHostGeneratedHandler(string normalizedPath)
    {
        const string hosts = "backend/Hosts/";
        const string generated = "/Internal/Generated/WolverineHandlers/";
        var marker = normalizedPath.StartsWith(hosts, StringComparison.Ordinal) ? 0 : normalizedPath.IndexOf("/" + hosts, StringComparison.Ordinal) + 1;
        if (marker <= 0 && !normalizedPath.StartsWith(hosts, StringComparison.Ordinal))
        {
            return false;
        }

        var host = marker + hosts.Length;
        var slash = normalizedPath.IndexOf('/', host);
        return slash > host
            && string.CompareOrdinal(normalizedPath, slash, generated, 0, generated.Length) == 0;
    }

    private static bool IsFeatureMigration(string normalizedPath) =>
        DunePaths.TryGetFeature(normalizedPath, out var feature)
        && DunePaths.ContainsOrdinal(normalizedPath, "backend/Features/" + feature + "/Persistence/Migrations/");

    public static bool IsComment(SyntaxTrivia trivia) => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia);

    public static IEnumerable<SyntaxTrivia> EnumerateComments(SyntaxNode root)
    {
        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
        {
            if (IsComment(trivia))
            {
                yield return trivia;
            }
        }
    }

    public static string ReplacementText(SyntaxTrivia trivia)
    {
        var text = trivia.ToFullString();
        if (text.IndexOf('\n') < 0 && text.IndexOf('\r') < 0)
        {
            if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
                || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia))
            {
                return string.Empty;
            }

            return new string(' ', text.Length);
        }

        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            builder.Append(character == '\r' || character == '\n' ? character : ' ');
        }

        return builder.ToString();
    }

    public static string RemoveComments(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var changes = new List<TextChange>();
        foreach (var trivia in EnumerateComments(tree.GetRoot()))
        {
            changes.Add(new TextChange(trivia.Span, ReplacementText(trivia)));
        }

        if (changes.Count == 0)
        {
            return source;
        }

        return tree.GetText().WithChanges(changes).ToString();
    }
}
