using Bagatka.Foundation;
using Xunit;

namespace Bagatka.Sandboxing.ConformanceTests;

internal static class TestResults
{
    public static T Value<T>(Result<T> result)
        where T : notnull
    {
        if (!result.TryGetValue(out T? value, out Error? error))
        {
            Assert.Fail("Expected success, got " + error.Code + ": " + error.Message);
        }

        return value;
    }

    public static Error ErrorOf<T>(Result<T> result)
        where T : notnull
    {
        Assert.False(result.TryGetValue(out T? _, out Error? error), "Expected an error, got success.");
        return error;
    }
}
