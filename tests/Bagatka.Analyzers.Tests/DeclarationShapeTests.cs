using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Bagatka.Analyzers.Tests;

public sealed class DeclarationShapeTests
{
    [Fact]
    public async Task Our_methods_declare_no_out_parameters_unless_their_signature_is_given()
    {
        const string sample = """
            using System.Collections;
            using System.Collections.Generic;
            using System.Diagnostics.CodeAnalysis;

            internal sealed record Point(int X, int Y);

            internal sealed class Pair(int left, int right)
            {
                public bool TrySplit(out int first) // BAG0003
                {
                    first = left;
                    return true;
                }

                public void Deconstruct(out int first, out int second)
                {
                    first = left;
                    second = right;
                }
            }

            internal sealed class Names : IReadOnlyDictionary<string, string>
            {
                public string this[string key] => key;
                public IEnumerable<string> Keys => [];
                public IEnumerable<string> Values => [];
                public int Count => 0;
                public bool ContainsKey(string key) => false;
                public bool TryGetValue(string key, [MaybeNullWhen(false)] out string value)
                {
                    value = null;
                    return false;
                }

                public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => new List<KeyValuePair<string, string>>().GetEnumerator();
                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }
            """;

        await Analysis.VerifyAsync(sample, OutputKind.DynamicallyLinkedLibrary);
    }

    [Fact]
    public async Task Local_functions_belong_only_to_top_level_programs()
    {
        const string sample = """
            using System;

            Console.WriteLine(Shout("hello"));

            static string Shout(string text) => text.ToUpperInvariant();

            internal static class Helpers
            {
                public static int Sum(int left, int right)
                {
                    return Add();

                    int Add() => left + right; // BAG0004
                }
            }
            """;

        await Analysis.VerifyAsync(sample, OutputKind.ConsoleApplication);
    }
}
