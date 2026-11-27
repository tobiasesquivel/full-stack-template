using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Dune.CommentVerifier;

public static class Program
{
    public static int Main(string[] args)
    {
        string? root = null;
        var fix = false;
        var dryRun = false;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument == "--root" && index + 1 < args.Length)
            {
                root = args[++index];
            }
            else if (argument == "--fix")
            {
                fix = true;
            }
            else if (argument == "--dry-run")
            {
                dryRun = true;
            }
            else if (argument == "--help" || argument == "-h")
            {
                Console.WriteLine("usage: Dune.CommentVerifier --root <repo> [--fix] [--dry-run]");
                return 2;
            }
            else
            {
                Console.Error.WriteLine("usage: Dune.CommentVerifier --root <repo> [--fix] [--dry-run]");
                return 2;
            }
        }

        if (string.IsNullOrWhiteSpace(root))
        {
            Console.Error.WriteLine("usage: Dune.CommentVerifier --root <repo> [--fix] [--dry-run]");
            return 2;
        }

        string resolvedRoot;
        try
        {
            resolvedRoot = Path.GetFullPath(root);
        }
        catch (Exception problem) when (problem is ArgumentException || problem is NotSupportedException || problem is PathTooLongException)
        {
            Console.Error.WriteLine("invalid --root: " + problem.Message);
            return 2;
        }

        if (!Directory.Exists(resolvedRoot))
        {
            Console.Error.WriteLine("root not found: " + resolvedRoot);
            return 2;
        }

        List<string> files;
        try
        {
            files = CollectFiles(resolvedRoot);
        }
        catch (Exception problem) when (problem is IOException || problem is UnauthorizedAccessException)
        {
            Console.Error.WriteLine("scan failed: " + problem.Message);
            return 2;
        }

        var violations = new List<(string Path, int Line)>();
        var fixedCount = 0;
        foreach (var file in files.OrderBy(candidate => candidate, StringComparer.Ordinal))
        {
            string source;
            try
            {
                source = File.ReadAllText(file);
            }
            catch (Exception problem) when (problem is IOException || problem is UnauthorizedAccessException)
            {
                Console.Error.WriteLine("read failed: " + file + ": " + problem.Message);
                return 2;
            }

            SyntaxTree tree;
            try
            {
                tree = CSharpSyntaxTree.ParseText(source, path: file);
            }
            catch (Exception problem)
            {
                Console.Error.WriteLine("parse failed: " + file + ": " + problem.Message);
                return 2;
            }

            var comments = Dune.Analyzers.DuneCommentPolicy.EnumerateComments(tree.GetRoot()).ToList();
            if (comments.Count == 0)
            {
                continue;
            }

            foreach (var trivia in comments)
            {
                violations.Add((file, trivia.GetLocation().GetLineSpan().StartLinePosition.Line + 1));
            }

            if (fix && !dryRun)
            {
                var changes = comments.Select(trivia =>
                    new TextChange(trivia.Span, Dune.Analyzers.DuneCommentPolicy.ReplacementText(trivia))).ToList();
                var cleaned = tree.GetText().WithChanges(changes).ToString();
                if (!string.Equals(cleaned, source, StringComparison.Ordinal))
                {
                    try
                    {
                        File.WriteAllText(file, cleaned);
                        fixedCount++;
                    }
                    catch (Exception problem) when (problem is IOException || problem is UnauthorizedAccessException)
                    {
                        Console.Error.WriteLine("fix failed: " + file + ": " + problem.Message);
                        return 2;
                    }
                }
            }
        }

        if (dryRun && violations.Count > 0)
        {
            Console.WriteLine(violations.Count + " comment(s) found (--dry-run, no changes).");
        }
        else if (fix && !dryRun && fixedCount > 0)
        {
            Console.WriteLine("removed comments in " + fixedCount + " file(s).");
        }

        foreach (var violation in violations.OrderBy(candidate => candidate.Path, StringComparer.Ordinal).ThenBy(candidate => candidate.Line))
        {
            Console.WriteLine(violation.Path + ":" + violation.Line + ": DUNE0009 comment");
        }

        return violations.Count > 0 ? 1 : 0;
    }

    private static List<string> CollectFiles(string root)
    {
        var backend = Path.Combine(root, "backend");
        var dune = Path.Combine(root, "dune");
        var results = new List<string>();
        if (Directory.Exists(backend))
        {
            CollectCsFiles(backend, results);
        }

        if (Directory.Exists(dune))
        {
            CollectCsFiles(dune, results);
        }

        return results
            .Where(file => !IsExcludedDirectory(file))
            .Where(file => !Dune.Analyzers.DuneCommentPolicy.IsExemptPath(Dune.Analyzers.DunePaths.Normalize(file), root))
            .ToList();
    }

    private static void CollectCsFiles(string directory, List<string> results)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
        {
            results.Add(file);
        }
    }

    private static bool IsExcludedDirectory(string path)
    {
        var normalized = Dune.Analyzers.DunePaths.Normalize(path);
        return Dune.Analyzers.DunePaths.ContainsOrdinal(normalized, "/bin/")
            || Dune.Analyzers.DunePaths.ContainsOrdinal(normalized, "/obj/")
            || Dune.Analyzers.DunePaths.ContainsOrdinal(normalized, "/.git/");
    }
}
