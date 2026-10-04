using Microsoft.CodeAnalysis;

namespace Bagatka.Analyzers;

// Every rule this analyzer reports. Each one enforces a line of PATTERNS.md, which says why.
internal static class Rules
{
    private const string Category = "Readability";

    public static readonly DiagnosticDescriptor AwaitStandsAlone = new DiagnosticDescriptor(
        id: "BAG0001",
        title: "An await is its statement's whole expression",
        messageFormat: "Await into a local first, then use the local",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "PATTERNS.md entry 1, one step per statement. Allowed: 'await X;', 'T x = await X;', 'x = await X;', 'return await X;', '=> await X', and a while condition reading the next item.");

    public static readonly DiagnosticDescriptor OutCallStandsAlone = new DiagnosticDescriptor(
        id: "BAG0002",
        title: "A call with an out argument is its statement's whole expression",
        messageFormat: "Call '{0}' in its own statement, such as 'bool parsed = ...;', then use the result",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "PATTERNS.md entry 1, no out in our own APIs. Allowed: as a statement, a local's initial value, an assignment's right side, and in a while condition reading the next item.");

    public static readonly DiagnosticDescriptor ThrowIsAStatement = new DiagnosticDescriptor(
        id: "BAG0008",
        title: "A throw is its own statement",
        messageFormat: "Check first, then throw from an if statement",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "PATTERNS.md entry 1, one step per statement. A switch arm, a lambda, or a member whose whole body throws may still use a throw expression.");

    public static readonly DiagnosticDescriptor NoOutParameters = new DiagnosticDescriptor(
        id: "BAG0003",
        title: "Our methods don't declare out parameters",
        messageFormat: "'{0}' declares an out parameter; return a nullable or a Result instead",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "PATTERNS.md entry 1, no out in our own APIs. Overrides, interface implementations, and Deconstruct follow the signature they must match.");

    public static readonly DiagnosticDescriptor NoLocalFunctions = new DiagnosticDescriptor(
        id: "BAG0004",
        title: "No local functions",
        messageFormat: "Make '{0}' a private method",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "PATTERNS.md entry 1. Top-level programs have no class for a private method, so they may declare local functions.");

    public static readonly DiagnosticDescriptor NoTargetTypedNew = new DiagnosticDescriptor(
        id: "BAG0005",
        title: "Object creation names its type",
        messageFormat: "Name the type: new {0}(...)",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "PATTERNS.md entry 1, types are written out.");

    public static readonly DiagnosticDescriptor NoUnionConversions = new DiagnosticDescriptor(
        id: "BAG0006",
        title: "Union values are created with new",
        messageFormat: "Create the value with new {0}(...) instead of converting to it",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "PATTERNS.md entry 1, no implicit conversions.");

    public static readonly DiagnosticDescriptor DictionaryLookup = new DiagnosticDescriptor(
        id: "BAG0007",
        title: "Dictionary lookups use GetValueOrDefault",
        messageFormat: "Use GetValueOrDefault for the value, or ContainsKey for membership",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "PATTERNS.md entry 1, no out in our own APIs. A rule here rather than a banned API, because source generators' output calls TryGetValue and banned APIs are checked there too.");
}
