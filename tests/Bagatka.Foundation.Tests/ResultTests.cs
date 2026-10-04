using System;
using Xunit;

namespace Bagatka.Foundation.Tests;

public sealed class ResultTests
{
    [Fact]
    public void A_success_has_its_output_and_no_error()
    {
        Result<string> result = new Result<string>("value");

        Assert.False(result.Failed);
        Assert.Equal("value", result.Output);
        Assert.Null(result.Error);
    }

    [Fact]
    public void A_failure_has_its_error()
    {
        Error notFound = Error.NotFound("users.not_found", "User not found.");
        Result<string> result = new Result<string>(notFound);

        Assert.True(result.Failed);
        Assert.Same(notFound, result.Error);
    }

    [Fact]
    public void Reading_the_output_of_a_failure_is_a_bug_and_throws()
    {
        Result<Guid> result = new Result<Guid>(Error.Forbidden);

        Assert.Throws<InvalidOperationException>(() => result.Output);
    }

    [Fact]
    public void A_result_without_a_value_tells_success_from_failure()
    {
        Result succeeded = new Result(new Success());
        Result failed = new Result(Error.Forbidden);

        Assert.False(succeeded.Failed);
        Assert.Null(succeeded.Error);
        Assert.True(failed.Failed);
        Assert.Same(Error.Forbidden, failed.Error);
    }

    [Fact]
    public void A_default_result_is_a_bug_and_throws()
    {
        Result<string> result = default;

        Assert.Throws<InvalidOperationException>(() => result.Output);
    }
}
