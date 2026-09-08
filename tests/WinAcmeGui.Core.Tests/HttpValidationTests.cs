using WinAcmeGui.Core;
using WinAcmeGui.Core.Models;
using WinAcmeGui.Core.Plugins;
using Xunit;

namespace WinAcmeGui.Core.Tests;

public class HttpValidationTests
{
    private static RenewalDefinition Definition(string pluginId, params (string Key, string Value)[] values)
    {
        var definition = TestData.ValidCloudflareDefinition();
        definition.Validation = new ValidationSettings { PluginId = pluginId };

        foreach (var (key, value) in values)
        {
            definition.Validation.Values[key] = value;
        }

        return definition;
    }

    private static IReadOnlyList<string> ArgumentsFor(RenewalDefinition definition) =>
        WacsArgumentBuilder.BuildCreateRenewal(TestData.WacsPath, definition).ArgumentValues;

    private static string ValueAfter(IReadOnlyList<string> args, string flag)
    {
        var index = args.ToList().IndexOf(flag);
        Assert.True(index >= 0, $"Expected the command line to contain {flag}.");
        return args[index + 1];
    }

    [Fact]
    public void SelfHostingAsksForTheHttpChallenge()
    {
        var args = ArgumentsFor(Definition(PluginCatalog.SelfHostingId));

        Assert.Equal("http-01", ValueAfter(args, "--validationmode"));
        Assert.Equal("selfhosting", ValueAfter(args, "--validation"));
    }

    [Fact]
    public void SelfHostingNeedsNothingConfigured()
    {
        var args = ArgumentsFor(Definition(PluginCatalog.SelfHostingId));

        Assert.DoesNotContain("--validationport", args);
        Assert.DoesNotContain("--validationprotocol", args);
        Assert.Empty(RenewalValidator.Validate(Definition(PluginCatalog.SelfHostingId)));
    }

    [Fact]
    public void SelfHostingPassesThePortAndProtocolWhenGiven()
    {
        var args = ArgumentsFor(Definition(
            PluginCatalog.SelfHostingId,
            ("validationport", "8080"),
            ("validationprotocol", "https")));

        Assert.Equal("8080", ValueAfter(args, "--validationport"));
        Assert.Equal("https", ValueAfter(args, "--validationprotocol"));
    }

    [Fact]
    public void FileSystemPassesTheWebRoot()
    {
        var args = ArgumentsFor(Definition(PluginCatalog.FileSystemId, ("webroot", @"C:\inetpub\wwwroot")));

        Assert.Equal("http-01", ValueAfter(args, "--validationmode"));
        Assert.Equal("filesystem", ValueAfter(args, "--validation"));
        Assert.Equal(@"C:\inetpub\wwwroot", ValueAfter(args, "--webroot"));
    }

    [Fact]
    public void FileSystemRequiresAWebRoot()
    {
        Assert.NotEmpty(RenewalValidator.Validate(Definition(PluginCatalog.FileSystemId)));
        Assert.Empty(RenewalValidator.Validate(
            Definition(PluginCatalog.FileSystemId, ("webroot", @"C:\inetpub\wwwroot"))));
    }

    [Fact]
    public void TheIisWebConfigSwitchIsABareFlag()
    {
        var args = ArgumentsFor(Definition(
            PluginCatalog.FileSystemId,
            ("webroot", @"C:\web"),
            ("manualtargetisiis", "true")));

        Assert.Contains("--manualtargetisiis", args);

        var off = ArgumentsFor(Definition(
            PluginCatalog.FileSystemId,
            ("webroot", @"C:\web"),
            ("manualtargetisiis", "false")));

        Assert.DoesNotContain("--manualtargetisiis", off);
    }

    // ------------------------------------------------------------- wildcards

    private static RenewalDefinition WithHosts(string pluginId, params string[] hosts)
    {
        var definition = pluginId == PluginCatalog.FileSystemId
            ? Definition(pluginId, ("webroot", @"C:\web"))
            : Definition(pluginId);

        definition.Certificate.HostNames = [.. hosts];
        definition.Certificate.CommonName = string.Empty;
        return definition;
    }

    [Fact]
    public void AWildcardCannotUseHttpValidation()
    {
        var problems = RenewalValidator.Validate(
            WithHosts(PluginCatalog.SelfHostingId, "*.example.com", "example.com"));

        Assert.Contains(problems, p => p.Contains("DNS validation", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("*.example.com", StringComparison.Ordinal));
    }

    [Fact]
    public void PlainHostNamesAreFineOverHttp()
    {
        Assert.Empty(RenewalValidator.Validate(
            WithHosts(PluginCatalog.SelfHostingId, "example.com", "www.example.com")));
    }

    [Fact]
    public void AWildcardIsFineOverDns()
    {
        var definition = TestData.ValidCloudflareDefinition();
        definition.Certificate.HostNames = ["*.example.com"];
        definition.Certificate.CommonName = string.Empty;

        Assert.Empty(RenewalValidator.Validate(definition));
    }

    // --------------------------------------------------------------- catalog

    [Fact]
    public void BothHttpPluginsShipInsideWinAcme()
    {
        foreach (var id in new[] { PluginCatalog.SelfHostingId, PluginCatalog.FileSystemId })
        {
            var plugin = PluginCatalog.Get(id);

            Assert.False(plugin.RequiresSeparateDownload);
            Assert.Equal("http-01", plugin.ValidationMode);
            Assert.False(plugin.UsesScript);
        }
    }

    [Fact]
    public void PluginsAreGroupedByChallenge()
    {
        var dns = PluginCatalog.ForChallenge(PluginCatalog.DnsChallenge).Select(p => p.Id).ToList();
        var http = PluginCatalog.ForChallenge(PluginCatalog.HttpChallenge).Select(p => p.Id).ToList();

        Assert.Contains(PluginCatalog.CloudflareId, dns);
        Assert.Contains(PluginCatalog.NamecheapId, dns);
        Assert.Contains(PluginCatalog.SelfHostingId, http);
        Assert.Contains(PluginCatalog.FileSystemId, http);

        Assert.DoesNotContain(PluginCatalog.SelfHostingId, dns);
        Assert.DoesNotContain(PluginCatalog.CloudflareId, http);
        Assert.Equal(PluginCatalog.ValidationPlugins.Count, dns.Count + http.Count);
    }

    [Fact]
    public void DetectionFlagsStayUniqueAcrossEveryPlugin()
    {
        var flags = PluginCatalog.ValidationPlugins.Select(p => p.EffectiveDetectionFlag).ToList();

        Assert.Equal(flags.Count, flags.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
