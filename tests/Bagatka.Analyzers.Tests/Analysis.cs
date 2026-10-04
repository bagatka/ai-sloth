using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Bagatka.Analyzers.Tests;

/// <summary>
/// Runs our analyzers on a sample and compares what they report with what the sample expects: a line
/// ending in <c>// BAG0001</c> expects that diagnostic, and every other line expects none.
/// </summary>
internal static class Analysis
{
    // The running runtime's assemblies, so samples can use Task, unions, and the rest of the BCL.
    private static readonly ImmutableArray<MetadataReference> References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
        .ToImmutableArray();

    private static readonly ImmutableArray<DiagnosticAnalyzer> Analyzers =
        ImmutableArray.Create<DiagnosticAnalyzer>(new StatementShapeAnalyzer(), new DeclarationShapeAnalyzer(), new ExplicitCreationAnalyzer(), new DictionaryLookupAnalyzer());

    /// <summary>Asserts the sample compiles and our analyzers report exactly the diagnostics it marks.</summary>
    public static async Task VerifyAsync(string sample, OutputKind kind)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(sample, new CSharpParseOptions(LanguageVersion.Preview), cancellationToken: TestContext.Current.CancellationToken);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Sample",
            [tree],
            References,
            new CSharpCompilationOptions(kind, nullableContextOptions: NullableContextOptions.Enable));
        List<string> compileErrors = compilation.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(Describe)
            .ToList();
        ImmutableArray<Diagnostic> reported = await compilation.WithAnalyzers(Analyzers).GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);

        Assert.Empty(compileErrors);
        Assert.Equal(Expected(sample), reported.Select(Describe).Order(StringComparer.Ordinal).ToList());
    }

    private static List<string> Expected(string sample)
    {
        const string Marker = "// BAG";
        string[] lines = sample.Split('\n');
        return Enumerable.Range(0, lines.Length)
            .Where(index => lines[index].Contains(Marker, StringComparison.Ordinal))
            .Select(index => lines[index][(lines[index].LastIndexOf(Marker, StringComparison.Ordinal) + 3)..].Trim() + " at line " + (index + 1).ToString(CultureInfo.InvariantCulture))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static string Describe(Diagnostic diagnostic)
    {
        int line = diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1;
        return diagnostic.Id + " at line " + line.ToString(CultureInfo.InvariantCulture);
    }
}
