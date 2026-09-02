using WinAcmeGui.Core;
using Xunit;

namespace WinAcmeGui.Core.Tests;

public class HostNameParserTests
{
    [Fact]
    public void SplitsOnNewlinesCommasAndSpaces()
    {
        var hosts = HostNameParser.Parse("example.com, www.example.com\nmail.example.com api.example.com");

        Assert.Equal(
            new[] { "example.com", "www.example.com", "mail.example.com", "api.example.com" },
            hosts);
    }

    [Fact]
    public void LowercasesAndRemovesDuplicatesKeepingOrder()
    {
        var hosts = HostNameParser.Parse("WWW.Example.com\nexample.com\nwww.example.COM");

        Assert.Equal(new[] { "www.example.com", "example.com" }, hosts);
    }

    [Fact]
    public void DropsTrailingDotsAndBlankEntries()
    {
        var hosts = HostNameParser.Parse("example.com.,,  \n\n www.example.com ");

        Assert.Equal(new[] { "example.com", "www.example.com" }, hosts);
    }

    [Fact]
    public void EmptyInputGivesEmptyList()
    {
        Assert.Empty(HostNameParser.Parse(null));
        Assert.Empty(HostNameParser.Parse("   \n\t "));
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("www.example.com")]
    [InlineData("a.b.c.example.co.uk")]
    [InlineData("*.example.com")]
    [InlineData("xn--80ak6aa92e.com")]
    [InlineData("EXAMPLE.COM")]
    [InlineData("host-name.example.com")]
    public void AcceptsRealHostNames(string host) => Assert.True(HostNameParser.IsValid(host));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("localhost")]
    [InlineData("example")]
    [InlineData("-bad.example.com")]
    [InlineData("bad-.example.com")]
    [InlineData("example..com")]
    [InlineData("exa mple.com")]
    [InlineData("192.168.0.1")]
    [InlineData("http://example.com")]
    [InlineData("*.*.example.com")]
    public void RejectsThingsThatAreNotHostNames(string? host) =>
        Assert.False(HostNameParser.IsValid(host));

    [Fact]
    public void RejectsOverlongLabel()
    {
        var label = new string('a', 64);
        Assert.False(HostNameParser.IsValid($"{label}.example.com"));
    }

    [Fact]
    public void DetectsWildcards()
    {
        Assert.True(HostNameParser.IsWildcard("*.example.com"));
        Assert.False(HostNameParser.IsWildcard("www.example.com"));
    }
}
