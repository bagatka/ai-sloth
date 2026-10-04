using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Bagatka.Analyzers;

/// <summary>
/// One step per statement: an await, or a call with an out argument, is the whole expression of its
/// statement, and a throw is a statement of its own. Conditions and arguments then only read values
/// computed before them.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StatementShapeAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rules.AwaitStandsAlone, Rules.OutCallStandsAlone, Rules.ThrowIsAStatement);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeAwait, SyntaxKind.AwaitExpression);
        context.RegisterSyntaxNodeAction(AnalyzeArgument, SyntaxKind.Argument);
        context.RegisterSyntaxNodeAction(AnalyzeThrow, SyntaxKind.ThrowExpression);
    }

    private static void AnalyzeAwait(SyntaxNodeAnalysisContext context)
    {
        AwaitExpressionSyntax awaited = (AwaitExpressionSyntax)context.Node;
        if (!StandsAlone(awaited))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rules.AwaitStandsAlone, awaited.GetLocation()));
        }
    }

    private static void AnalyzeArgument(SyntaxNodeAnalysisContext context)
    {
        ArgumentSyntax argument = (ArgumentSyntax)context.Node;
        bool isOut = argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword);

        // The argument list's owner: a call or an object creation. A constructor initializer
        // (base(out x)) isn't an expression and has nowhere else to go.
        ExpressionSyntax? call = argument.Parent?.Parent as ExpressionSyntax;
        if (!isOut || call is null || StandsAlone(call))
        {
            return;
        }

        string name = call is InvocationExpressionSyntax invocation ? invocation.Expression.ToString() : call.ToString();
        context.ReportDiagnostic(Diagnostic.Create(Rules.OutCallStandsAlone, call.GetLocation(), name));
    }

    // `value ?? throw ...` and `ok ? value : throw ...` read a value and fail in one expression. A
    // switch arm, a lambda, or a member whose whole body throws stays allowed.
    private static void AnalyzeThrow(SyntaxNodeAnalysisContext context)
    {
        ThrowExpressionSyntax thrown = (ThrowExpressionSyntax)context.Node;
        bool insideAnotherExpression = thrown.Parent is BinaryExpressionSyntax or ConditionalExpressionSyntax;
        if (insideAnotherExpression)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rules.ThrowIsAStatement, thrown.GetLocation()));
        }
    }

    // Whether the expression is its statement's whole step: the statement itself, a local's initial
    // value, an assignment's right side, what a member or lambda returns, or part of a loop condition.
    private static bool StandsAlone(ExpressionSyntax expression)
    {
        switch (expression.Parent)
        {
            case ExpressionStatementSyntax:
            case EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax }:
            case ReturnStatementSyntax:
            case YieldStatementSyntax:
            case ArrowExpressionClauseSyntax:
                return true;
            case AssignmentExpressionSyntax assignment:
                return assignment.Right == expression && assignment.Parent is ExpressionStatementSyntax;
            case LambdaExpressionSyntax lambda:
                return lambda.ExpressionBody == expression;
            default:
                return InLoopCondition(expression);
        }
    }

    // A loop condition may read the next item: while (await stream.MoveNextAsync()). The walk stops
    // at arguments and lambdas, so a read nested in a call inside the condition is still reported.
    private static bool InLoopCondition(ExpressionSyntax expression)
    {
        SyntaxNode node = expression;
        while (node.Parent is ExpressionSyntax parent && parent is not AnonymousFunctionExpressionSyntax)
        {
            node = parent;
        }

        switch (node.Parent)
        {
            case WhileStatementSyntax loop:
                return loop.Condition == node;
            case DoStatementSyntax loop:
                return loop.Condition == node;
            default:
                return false;
        }
    }
}
