using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Bagatka.Analyzers;

/// <summary>
/// Creation the reader can see: object creation names its type, and union values are created with
/// <c>new</c> rather than converted from one of their cases.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExplicitCreationAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rules.NoTargetTypedNew, Rules.NoUnionConversions);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeTargetTypedNew, SyntaxKind.ImplicitObjectCreationExpression);
        context.RegisterOperationAction(AnalyzeConversion, OperationKind.Conversion);
    }

    private static void AnalyzeTargetTypedNew(SyntaxNodeAnalysisContext context)
    {
        ITypeSymbol? type = context.SemanticModel.GetTypeInfo(context.Node, context.CancellationToken).Type;
        string name = type is null ? "T" : type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        context.ReportDiagnostic(Diagnostic.Create(Rules.NoTargetTypedNew, context.Node.GetLocation(), name));
    }

    // Converting from another type to a union can only be a union conversion: no type in this
    // codebase declares a conversion operator. Roslyn's own IsUnion flag is still experimental.
    private static void AnalyzeConversion(OperationAnalysisContext context)
    {
        IConversionOperation conversion = (IConversionOperation)context.Operation;
        ITypeSymbol? target = conversion.Type;
        ITypeSymbol? source = conversion.Operand.Type;
        if (target is null || source is null || conversion.Conversion.IsIdentity)
        {
            return;
        }

        // An expression that doesn't compile already has an error; this rule would only add noise.
        if (source.TypeKind == TypeKind.Error)
        {
            return;
        }

        bool unionFromCase = IsUnion(target) && !IsUnion(source);
        if (unionFromCase)
        {
            string name = target.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            context.ReportDiagnostic(Diagnostic.Create(Rules.NoUnionConversions, conversion.Syntax.GetLocation(), name));
        }
    }

    // The compiler marks every union with this interface.
    private static bool IsUnion(ITypeSymbol type)
    {
        return type.AllInterfaces.Any(contract => string.Equals(
            contract.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            "global::System.Runtime.CompilerServices.IUnion",
            StringComparison.Ordinal));
    }
}
