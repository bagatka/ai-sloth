using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Bagatka.Analyzers;

/// <summary>
/// Declarations that hide steps: methods with out or ref parameters, whose callers can't see what
/// changes, and local functions, which hide a helper inside the method that calls it.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DeclarationShapeAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rules.NoOutParameters, Rules.NoLocalFunctions);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
        context.RegisterSyntaxNodeAction(AnalyzeLocalFunction, SyntaxKind.LocalFunctionStatement);
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context)
    {
        IMethodSymbol method = (IMethodSymbol)context.Symbol;
        bool declaresOut = method.Parameters.Any(parameter => parameter.RefKind is RefKind.Out or RefKind.Ref);

        // These follow a signature someone else defined.
        bool signatureIsGiven = method.IsImplicitlyDeclared
            || method.IsOverride
            || string.Equals(method.Name, "Deconstruct", System.StringComparison.Ordinal)
            || ImplementsInterface(method);
        if (declaresOut && !signatureIsGiven)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rules.NoOutParameters, method.Locations[0], method.Name));
        }
    }

    private static bool ImplementsInterface(IMethodSymbol method)
    {
        if (!method.ExplicitInterfaceImplementations.IsEmpty)
        {
            return true;
        }

        INamedTypeSymbol type = method.ContainingType;
        return type.AllInterfaces
            .SelectMany(contract => contract.GetMembers(method.Name))
            .Any(member => SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(member), method));
    }

    private static void AnalyzeLocalFunction(SyntaxNodeAnalysisContext context)
    {
        LocalFunctionStatementSyntax local = (LocalFunctionStatementSyntax)context.Node;

        // A top-level program has no class to hold a private method.
        bool inTopLevelProgram = local.Parent is GlobalStatementSyntax;
        if (!inTopLevelProgram)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rules.NoLocalFunctions, local.Identifier.GetLocation(), local.Identifier.ValueText));
        }
    }
}
