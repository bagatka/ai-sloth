using System.Globalization;
using Xunit;

namespace Bagatka.AiSloth.ArchitectureTests;

public sealed class TestCultureTests
{
    // Proves tests/xunit.runner.json is applied; without it, culture bugs pass on English machines.
    [Fact]
    public void Tests_run_under_an_unfriendly_culture()
    {
        Assert.Equal("tr-TR", CultureInfo.CurrentCulture.Name);
    }
}
