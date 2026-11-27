using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Dune.Analyzers;

internal static class DuneExemptions
{
    public const string AttributeName = "DuneExemptionAttribute";
    public const string AttributeNamespace = "TemplateApp.Shared.Architecture";

    public const string IdempotentPost = "IdempotentPost";
    public const string ReadSliceLoadsEntity = "ReadSliceLoadsEntity";
    public const string MediatorEndpoint = "MediatorEndpoint";
    public const string TransactionOwner = "TransactionOwner";

    public static bool Exempts(INamedTypeSymbol? type, string check) =>
        type is not null
        && Read(type).Any(exemption => check.Equals(exemption.Check, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(exemption.Reason));

    public static IEnumerable<(AttributeData Attribute, string? Check, string? Reason)> Read(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass is not { } attributeClass
                || !attributeClass.Name.Equals(AttributeName, StringComparison.Ordinal)
                || !DuneSymbols.NamespaceOf(attributeClass).Equals(AttributeNamespace, StringComparison.Ordinal))
            {
                continue;
            }

            var arguments = attribute.ConstructorArguments;
            yield return (
                attribute,
                arguments.Length > 0 ? EnumMemberName(arguments[0]) : null,
                arguments.Length > 1 ? arguments[1].Value as string : null);
        }
    }

    public static INamedTypeSymbol? TopLevelType(ISymbol? symbol)
    {
        INamedTypeSymbol? type = null;
        for (var current = symbol; current is not null; current = current.ContainingSymbol)
        {
            if (current is INamedTypeSymbol named)
            {
                type = named;
            }
        }

        return type;
    }

    public static INamedTypeSymbol? FeatureType(Compilation compilation, INamedTypeSymbol member, string feature)
    {
        var ns = DuneSymbols.NamespaceOf(member);
        var marker = ".Features." + feature;
        var index = ns.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0 || (ns.Length > index + marker.Length && ns[index + marker.Length] != '.'))
        {
            return null;
        }

        return compilation.GetTypeByMetadataName(ns.Substring(0, index + marker.Length) + "." + feature + "Feature");
    }

    private static string? EnumMemberName(TypedConstant constant) =>
        constant.Type is INamedTypeSymbol { TypeKind: TypeKind.Enum } type
            ? type.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(field => field.HasConstantValue && Equals(field.ConstantValue, constant.Value))?.Name
            : null;
}
