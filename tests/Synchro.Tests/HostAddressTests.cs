using Synchro.Core.Servers;
using Xunit;

namespace Synchro.Tests;

public class HostAddressTests
{
    [Theory]
    // Host names (RFC 1123)
    [InlineData("nas")]
    [InlineData("nas.local")]
    [InlineData("NAS.Local")]
    [InlineData("my-nas.home.arpa")]
    [InlineData("3com.example")]          // RFC 1123: a label may start with a digit
    [InlineData("nas.local.")]            // fully qualified, with the root dot
    [InlineData("xn--mnchen-3ya.local")]
    [InlineData("münchen.local")]         // checked as its ASCII form (RFC 5890)
    // IPv4 (RFC 3986 dec-octet)
    [InlineData("192.168.1.20")]
    [InlineData("0.0.0.0")]
    [InlineData("255.255.255.255")]
    [InlineData("10.0.0.1")]
    // IPv6 (RFC 4291), brackets (RFC 3986) and zones (RFC 4007, 6874)
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("fd00::1")]
    [InlineData("2001:db8::8a2e:370:7334")]
    [InlineData("2001:0db8:0000:0000:0000:8a2e:0370:7334")]
    [InlineData("FE80::1")]
    [InlineData("fe80::1%en0")]
    [InlineData("fe80::1%eth0.10")]
    [InlineData("[::1]")]
    [InlineData("[fe80::1%en0]")]
    [InlineData("::ffff:192.168.1.20")]
    [InlineData("64:ff9b::192.0.2.33")]
    [InlineData("1:2:3:4:5:6:7::")]
    [InlineData("1:2:3:4:5:6:1.2.3.4")]
    // Surrounding spaces are trimmed when saving
    [InlineData(" nas.local ")]
    public void Accepts_valid_addresses(string host) => Assert.Null(HostAddress.Validate(host));

    [Theory]
    [InlineData("1921.168.1.20", "1921 is too large")]                  // the typo that started this
    [InlineData("192.168.1.256", "256 is too large")]
    [InlineData("192.168.1", "four numbers")]
    [InlineData("192.168.1.20.5", "four numbers")]
    [InlineData("192.168.01.20", "without leading zeros")]
    [InlineData("1.2.3.4.", "only digits")]
    [InlineData("nas.123", "only digits")]                              // RFC 3696: no all-numeric top label
    [InlineData("0x7f.0.0.1", "only digits")]
    [InlineData("nas..local", "empty part")]
    [InlineData(".nas", "empty part")]
    [InlineData("-nas.local", "hyphen")]
    [InlineData("nas-.local", "hyphen")]
    [InlineData("my_nas.local", "\"_\" isn't allowed")]
    [InlineData("nas!", "\"!\" isn't allowed")]
    [InlineData("nas local", "spaces")]
    [InlineData("http://nas.local", "without http://")]
    [InlineData("nas.local/transmission", "without http://")]
    [InlineData("nas.local:9091", "Port field")]
    [InlineData("192.168.1.20:9091", "Port field")]
    [InlineData("1::2::3", "IPv6")]
    [InlineData("1:2:3:4:5:6:7:8:9", "IPv6")]
    [InlineData("1:2:3:4:5:6:7", "IPv6")]
    [InlineData("12345::1", "IPv6")]
    [InlineData("fe80::g", "IPv6")]
    [InlineData(":1::", "IPv6")]
    [InlineData("::ffff:192.168.1.256", "IPv6")]
    [InlineData("fe80::1%", "IPv6")]
    [InlineData("[::1", "IPv6")]
    public void Explains_what_is_wrong(string host, string expected)
        => Assert.Contains(expected, HostAddress.Validate(host));

    [Fact]
    public void Limits_label_and_name_lengths()
    {
        Assert.Null(HostAddress.Validate(new string('a', 63) + ".local"));
        Assert.Contains("63", HostAddress.Validate(new string('a', 64) + ".local"));

        var longName = string.Join('.', Enumerable.Repeat(new string('a', 63), 4)); // 255 characters
        Assert.Contains("253", HostAddress.Validate(longName));
    }

    [Fact]
    public void Empty_is_left_to_the_required_check()
    {
        Assert.Null(HostAddress.Validate(""));
        Assert.False(HostAddress.IsValid(""));
    }
}
