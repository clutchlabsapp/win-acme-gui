using WinAcmeGui.Core;
using Xunit;

namespace WinAcmeGui.Core.Tests;

public class WinAcmeReleaseTests
{
    /// <summary>
    /// Shaped like the real GitHub payload, using the actual asset names from the
    /// win-acme v2.2.9.1701 release.
    /// </summary>
    private const string SampleJson = """
    {
      "tag_name": "v2.2.9.1701",
      "name": "Release 2.2.9.1701",
      "assets": [
        { "name": "win-acme.v2.2.9.1701.x64.trimmed.zip",
          "size": 12345678,
          "browser_download_url": "https://github.com/win-acme/win-acme/releases/download/v2.2.9.1701/win-acme.v2.2.9.1701.x64.trimmed.zip" },
        { "name": "win-acme.v2.2.9.1701.x64.pluggable.zip",
          "size": 23456789,
          "browser_download_url": "https://github.com/win-acme/win-acme/releases/download/v2.2.9.1701/win-acme.v2.2.9.1701.x64.pluggable.zip" },
        { "name": "win-acme.v2.2.9.1701.arm64.pluggable.zip",
          "size": 23456000,
          "browser_download_url": "https://github.com/win-acme/win-acme/releases/download/v2.2.9.1701/win-acme.v2.2.9.1701.arm64.pluggable.zip" },
        { "name": "win-acme.v2.2.9.1701.x86.pluggable.zip",
          "size": 22000000,
          "browser_download_url": "https://github.com/win-acme/win-acme/releases/download/v2.2.9.1701/win-acme.v2.2.9.1701.x86.pluggable.zip" },
        { "name": "plugin.validation.dns.cloudflare.v2.2.9.1701.zip",
          "size": 54321,
          "browser_download_url": "https://github.com/win-acme/win-acme/releases/download/v2.2.9.1701/plugin.validation.dns.cloudflare.v2.2.9.1701.zip" },
        { "name": "plugin.validation.dns.route53.v2.2.9.1701.zip",
          "size": 65432,
          "browser_download_url": "https://github.com/win-acme/win-acme/releases/download/v2.2.9.1701/plugin.validation.dns.route53.v2.2.9.1701.zip" },
        { "name": "plugin.validation.dns.azure.v2.2.9.1701.zip",
          "size": 76543,
          "browser_download_url": "https://github.com/win-acme/win-acme/releases/download/v2.2.9.1701/plugin.validation.dns.azure.v2.2.9.1701.zip" },
        { "name": "plugin.validation.dns.godaddy.v2.2.9.1701.zip",
          "size": 45678,
          "browser_download_url": "https://github.com/win-acme/win-acme/releases/download/v2.2.9.1701/plugin.validation.dns.godaddy.v2.2.9.1701.zip" },
        { "name": "win-acme.2.2.9.1701.nupkg",
          "size": 999,
          "browser_download_url": "https://github.com/win-acme/win-acme/releases/download/v2.2.9.1701/win-acme.2.2.9.1701.nupkg" }
      ]
    }
    """;

    private static WinAcmeRelease Sample() => WinAcmeRelease.Parse(SampleJson);

    [Fact]
    public void ReadsTheTagAndStripsTheLeadingV()
    {
        var release = Sample();

        Assert.Equal("v2.2.9.1701", release.TagName);
        Assert.Equal("2.2.9.1701", release.Version);
        Assert.Equal(9, release.Assets.Count);
    }

    [Fact]
    public void PicksThePluggableBuildForTheRequestedArchitecture()
    {
        var main = Sample().MainPackage("x64");

        Assert.NotNull(main);
        Assert.Equal("win-acme.v2.2.9.1701.x64.pluggable.zip", main.Name);
    }

    [Fact]
    public void NeverPicksTheTrimmedBuildWhenPluggableIsWanted()
    {
        // The trimmed build cannot load the DNS plugins, so choosing it would produce
        // a win-acme that silently lacks the user's provider.
        foreach (var architecture in new[] { "x64", "x86", "arm64" })
        {
            var main = Sample().MainPackage(architecture);
            Assert.NotNull(main);
            Assert.DoesNotContain("trimmed", main.Name, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(architecture, main.Name, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void CanStillAskForTheTrimmedBuildExplicitly()
    {
        var main = Sample().MainPackage("x64", pluggable: false);

        Assert.NotNull(main);
        Assert.Equal("win-acme.v2.2.9.1701.x64.trimmed.zip", main.Name);
    }

    [Fact]
    public void ReturnsNothingForAnArchitectureThatWasNotBuilt()
    {
        Assert.Null(Sample().MainPackage("mips"));
    }

    [Theory]
    [InlineData("cloudflare", "plugin.validation.dns.cloudflare.v2.2.9.1701.zip")]
    [InlineData("route53", "plugin.validation.dns.route53.v2.2.9.1701.zip")]
    [InlineData("azure", "plugin.validation.dns.azure.v2.2.9.1701.zip")]
    public void FindsTheDownloadForEachSupportedDnsPlugin(string pluginId, string expected)
    {
        var plugin = Sample().DnsPlugin(pluginId);

        Assert.NotNull(plugin);
        Assert.Equal(expected, plugin.Name);
    }

    [Fact]
    public void APluginNameIsMatchedExactlyRatherThanByPrefix()
    {
        // "dns" must not match "dns.cloudflare...", and a missing plugin is null.
        Assert.Null(Sample().DnsPlugin("dns"));
        Assert.Null(Sample().DnsPlugin("namecheap"));
    }

    [Fact]
    public void SizesAreReportedInHumanUnits()
    {
        Assert.Equal("22.4 MB", Sample().MainPackage("x64")!.SizeDescription);
        Assert.Equal("53 KB", Sample().DnsPlugin("cloudflare")!.SizeDescription);
    }

    [Theory]
    [InlineData("https://github.com/win-acme/win-acme/releases/download/v1/x.zip", true)]
    [InlineData("https://objects.githubusercontent.com/whatever", true)]
    [InlineData("http://github.com/win-acme/win-acme/releases/download/v1/x.zip", false)]
    [InlineData("https://github.com.evil.example/x.zip", false)]
    [InlineData("https://example.com/win-acme.zip", false)]
    public void OnlyGitHubReleaseDownloadsOverHttpsAreTrusted(string url, bool trusted)
    {
        Assert.Equal(trusted, WinAcmeRelease.IsTrustedDownload(new Uri(url)));
    }

    [Fact]
    public void MalformedPayloadsDoNotThrow()
    {
        Assert.Empty(WinAcmeRelease.Parse("{}").Assets);
        Assert.Empty(WinAcmeRelease.Parse("""{"tag_name":"v1","assets":[]}""").Assets);

        // An asset missing its download url is skipped rather than breaking the rest.
        var partial = WinAcmeRelease.Parse("""
        {"tag_name":"v1","assets":[{"name":"a.zip"},{"name":"b.zip","browser_download_url":"https://github.com/b.zip"}]}
        """);

        Assert.Single(partial.Assets);
        Assert.Equal("b.zip", partial.Assets[0].Name);
    }
}
