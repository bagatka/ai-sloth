using System.Net;
using Bagatka.Foundation.Web;
using Xunit;

namespace Bagatka.Foundation.Tests;

public sealed class PublicNetworksTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.20.0.5")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.100.100.200")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("::1")]
    [InlineData("fd00::1")]
    [InlineData("fe80::1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("::ffff:127.0.0.1")]
    public void Private_and_local_addresses_are_refused(string address)
    {
        Assert.False(PublicNetworks.IsPublic(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")]
    [InlineData("100.128.0.1")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("::ffff:8.8.8.8")]
    public void Public_addresses_are_allowed(string address)
    {
        Assert.True(PublicNetworks.IsPublic(IPAddress.Parse(address)));
    }
}
