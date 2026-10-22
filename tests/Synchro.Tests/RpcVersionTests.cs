using Synchro.Core.Servers;
using Xunit;

namespace Synchro.Tests;

public class RpcVersionTests
{
    [Theory]
    [InlineData("6.0.0", true)]
    [InlineData("6.1.0", true)]
    [InlineData("6.1.0-beta.1", true)]
    [InlineData("5.3.0", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("garbage", false)]
    public void Only_6_0_0_and_newer_is_supported(string? semver, bool expected)
        => Assert.Equal(expected, RpcVersion.IsSupported(semver));

    [Fact]
    public void A_two_part_version_reads_as_three_parts()
    {
        Assert.True(RpcVersion.TryParse("6.1", out var version));
        Assert.Equal(new Version(6, 1, 0), version);
    }

    [Theory]
    [InlineData("6.0.0", false)]
    [InlineData("6.9.3", false)]
    [InlineData("7.0.0", true)]
    [InlineData("7.0.0-beta.1", true)]
    public void A_newer_major_version_is_untested_and_says_so(string semver, bool untested)
    {
        RpcVersion.TryParse(semver, out var version);
        var capabilities = new ServerCapabilities(version);

        Assert.Equal(untested, capabilities.IsUntested);
        Assert.Equal(untested, capabilities.Warning?.Contains("7.0.0") ?? false);
    }

    [Fact]
    public void At_least_compares_major_and_minor()
    {
        var capabilities = new ServerCapabilities(new Version(6, 2, 0));

        Assert.True(capabilities.AtLeast(6, 0));
        Assert.True(capabilities.AtLeast(6, 2));
        Assert.False(capabilities.AtLeast(6, 3));
        Assert.False(capabilities.AtLeast(7, 0));
        Assert.Equal("6.2.0", capabilities.RpcText);
    }
}
