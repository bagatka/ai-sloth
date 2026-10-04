using System.Threading.Tasks;
using Bagatka.Foundation;
using Xunit;

namespace Bagatka.Sandboxing.ConformanceTests;

internal static class TestResults
{
    public static async Task<T> ValueAsync<T>(Task<Result<T>> operation)
        where T : notnull
    {
        Result<T> result = await operation;
        if (result.Failed)
        {
            Assert.Fail("Expected success, got " + result.Error.Code + ": " + result.Error.Message);
        }

        return result.Output;
    }

    public static async Task<Error> ErrorOfAsync<T>(Task<Result<T>> operation)
        where T : notnull
    {
        Result<T> result = await operation;
        Assert.True(result.Failed, "Expected an error, got success.");
        return result.Error;
    }
}
