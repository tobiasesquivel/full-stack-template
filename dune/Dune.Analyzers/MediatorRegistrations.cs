using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Dune.Analyzers;

internal static class MediatorRegistrations
{
    public const string Atomic = "Atomic";
    public const string Eager = "Eager";
    public const string None = "None";

    public static bool TryRead(IInvocationOperation invocation, out INamedTypeSymbol handler, out string transaction)
    {
        handler = null!;
        transaction = string.Empty;
        var method = invocation.TargetMethod;
        if (!method.Name.Equals("AddMediatorHandler", StringComparison.Ordinal)
            || !method.ContainingType.Name.Equals("MessagingRegistration", StringComparison.Ordinal)
            || !DuneSymbols.NamespaceOf(method.ContainingType).Equals("TemplateApp.Infrastructure.Messaging", StringComparison.Ordinal)
            || method.TypeArguments.Length != 1
            || method.TypeArguments[0] is not INamedTypeSymbol registered)
        {
            return false;
        }

        foreach (var argument in invocation.Arguments)
        {
            var value = argument.Value;
            while (value is IConversionOperation conversion)
            {
                value = conversion.Operand;
            }

            if (value is IFieldReferenceOperation field && field.Field.ContainingType.Name.Equals("MediatorTransaction", StringComparison.Ordinal))
            {
                transaction = field.Field.Name;
            }
        }

        handler = registered;
        return transaction.Length > 0;
    }

    public static INamedTypeSymbol? EnclosingType(IOperation operation)
    {
        var symbol = operation.SemanticModel?.GetEnclosingSymbol(operation.Syntax.SpanStart);
        while (symbol is not null and not INamedTypeSymbol)
        {
            symbol = symbol.ContainingSymbol;
        }

        return symbol as INamedTypeSymbol;
    }
}
