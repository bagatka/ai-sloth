using System;
using Xunit;

namespace Bagatka.Foundation.Tests;

public sealed class ResultTests
{
    [Fact]
    public void TryGetValue_returns_the_value_of_a_success()
    {
        Result<string> result = new Result<string>("value");

        bool succeeded = result.TryGetValue(out string? value, out Error? error);

        Assert.True(succeeded);
        Assert.Equal("value", value);
        Assert.Null(error);
    }

    [Fact]
    public void TryGetValue_returns_the_error_of_a_failure()
    {
        Error notFound = Error.NotFound("users.not_found", "User not found.");
        Result<string> result = new Result<string>(notFound);

        bool succeeded = result.TryGetValue(out string? value, out Error? error);

        Assert.False(succeeded);
        Assert.Null(value);
        Assert.Same(notFound, error);
    }

    [Fact]
    public void IsError_is_false_for_a_success()
    {
        Result result = new Result(new Success());

        Assert.False(result.IsError(out Error? error));
        Assert.Null(error);
    }

    [Fact]
    public void IsError_returns_the_error_of_a_failure()
    {
        Result result = new Result(Error.Forbidden);

        Assert.True(result.IsError(out Error? error));
        Assert.Same(Error.Forbidden, error);
    }

    [Fact]
    public void A_default_result_is_a_bug_and_throws()
    {
        Result<string> result = default;

        Assert.Throws<InvalidOperationException>(() => result.TryGetValue(out string? _, out Error? _));
    }
}
