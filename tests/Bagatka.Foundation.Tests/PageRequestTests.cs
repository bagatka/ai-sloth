using Xunit;

namespace Bagatka.Foundation.Tests;

public sealed class PageRequestTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(50, 50)]
    [InlineData(10_000, PageRequest.MaxLimit)]
    public void Limit_is_clamped_to_a_bounded_page(int requested, int expected)
    {
        PageRequest page = new PageRequest(null, requested);

        Assert.Equal(expected, page.Limit);
    }
}
