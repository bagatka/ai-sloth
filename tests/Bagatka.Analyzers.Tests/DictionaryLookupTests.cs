using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Bagatka.Analyzers.Tests;

public sealed class DictionaryLookupTests
{
    [Fact]
    public async Task Dictionary_lookups_use_GetValueOrDefault()
    {
        const string sample = """
            using System.Collections.Concurrent;
            using System.Collections.Generic;

            internal static class Sample
            {
                public static string? Find(Dictionary<string, string> names, IReadOnlyDictionary<string, string> aliases, ConcurrentDictionary<string, string> cache)
                {
                    string? name = names.GetValueOrDefault("alice");
                    bool known = aliases.ContainsKey("bob");
                    bool found = names.TryGetValue("carol", out string? carol); // BAG0007
                    bool aliased = aliases.TryGetValue("dave", out string? dave); // BAG0007
                    bool cached = cache.TryGetValue("erin", out string? erin); // BAG0007
                    return known && found && aliased && cached ? name + carol + dave + erin : null;
                }
            }
            """;

        await Analysis.VerifyAsync(sample, OutputKind.DynamicallyLinkedLibrary);
    }
}
