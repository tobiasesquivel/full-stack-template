using System;
using Microsoft.CodeAnalysis;

namespace Dune.Analyzers;

// Symbol helpers shared by the layer analyzers. Every check resolves the
// bound symbol first, so using aliases and fully qualified names cannot
// bypass a rule that a plain name would trigger.
internal static class DuneSymbols
{
    public static string NamespaceOf(ISymbol symbol) =>
        symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;

    public static bool InNamespace(ISymbol symbol, string root)
    {
        var ns = NamespaceOf(symbol);
        return ns.Equals(root, StringComparison.Ordinal)
            || ns.StartsWith(root + ".", StringComparison.Ordinal);
    }

    public static bool ContainsSegment(string dotted, string segment) =>
        dotted.IndexOf(segment, StringComparison.Ordinal) >= 0;

    public static bool IsDbSet(INamedTypeSymbol type) =>
        type.Name.Equals("DbSet", StringComparison.Ordinal)
        && type.Arity == 1
        && NamespaceOf(type.OriginalDefinition).Equals("Microsoft.EntityFrameworkCore", StringComparison.Ordinal);

    public static bool DerivesFromDbContext(INamedTypeSymbol type, Compilation compilation)
    {
        var dbContext = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContext");
        if (dbContext is null)
        {
            return false;
        }

        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, dbContext))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsUserManagerFamily(INamedTypeSymbol type)
    {
        var name = type.OriginalDefinition.Name;
        var isManager = name.Equals("UserManager", StringComparison.Ordinal)
            || name.Equals("RoleManager", StringComparison.Ordinal)
            || name.Equals("SignInManager", StringComparison.Ordinal)
            || name.Equals("UserStore", StringComparison.Ordinal)
            || name.Equals("RoleStore", StringComparison.Ordinal);
        return isManager && InNamespace(type.OriginalDefinition, "Microsoft.AspNetCore.Identity");
    }

    // Entity Framework, any Persistence configuration, a DbContext subclass,
    // or an Identity manager type. Used by the endpoint rule.
    public static bool TouchesPersistenceOrEf(ISymbol symbol, Compilation compilation)
    {
        if (symbol is INamedTypeSymbol named)
        {
            if (named.TypeKind is TypeKind.TypeParameter)
            {
                return false;
            }

            if (DerivesFromDbContext(named, compilation)
                || IsUserManagerFamily(named)
                || InNamespace(named.OriginalDefinition, "Microsoft.EntityFrameworkCore"))
            {
                return true;
            }
        }

        if (symbol is IMethodSymbol method)
        {
            return TouchesPersistenceOrEf(method.ContainingType, compilation);
        }

        if (symbol is IPropertySymbol property)
        {
            return TouchesPersistenceOrEf(property.ContainingType, compilation)
                || TouchesPersistenceOrEf(property.Type, compilation);
        }

        if (symbol is IFieldSymbol field)
        {
            return TouchesPersistenceOrEf(field.ContainingType, compilation)
                || TouchesPersistenceOrEf(field.Type, compilation);
        }

        var ns = NamespaceOf(symbol);
        return ContainsSegment(ns, ".Persistence.")
            || ns.EndsWith(".Persistence", StringComparison.Ordinal);
    }

}
