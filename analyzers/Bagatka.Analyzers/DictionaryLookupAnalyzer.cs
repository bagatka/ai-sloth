using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Bagatka.Analyzers;

/// <summary>
/// Dictionary lookups read as values: <c>GetValueOrDefault</c> for a value and <c>ContainsKey</c> for
/// membership, never <c>TryGetValue</c> with its out argument.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DictionaryLookupAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rules.DictionaryLookup);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        IInvocationOperation invocation = (IInvocationOperation)context.Operation;
        IMethodSymbol method = invocation.TargetMethod;
        bool isTryGetValue = string.Equals(method.Name, "TryGetValue", StringComparison.Ordinal);
        if (isTryGetValue && IsDictionary(method.ContainingType))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rules.DictionaryLookup, invocation.Syntax.GetLocation()));
        }
    }

    private static bool IsDictionary(INamedTypeSymbol type)
    {
        return IsDictionaryInterface(type) || type.AllInterfaces.Any(IsDictionaryInterface);
    }

    private static bool IsDictionaryInterface(INamedTypeSymbol type)
    {
        bool isGeneric = string.Equals(type.ContainingNamespace?.ToDisplayString(), "System.Collections.Generic", StringComparison.Ordinal);
        return isGeneric && type.MetadataName is "IDictionary`2" or "IReadOnlyDictionary`2";
    }
}
