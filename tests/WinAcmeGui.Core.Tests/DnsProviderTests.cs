using WinAcmeGui.Core;
using WinAcmeGui.Core.Models;
using WinAcmeGui.Core.Plugins;
using Xunit;

namespace WinAcmeGui.Core.Tests;

/// <summary>
/// Covers the providers added from win-acme's own argument reference, and the traps
/// that come with them.
/// </summary>
public class DnsProviderTests
{
    private static IReadOnlyList<string> ArgumentsFor(string pluginId, params (string Key, string Value)[] values)
    {
        var definition = TestData.ValidCloudflareDefinition();
        definition.Validation = new ValidationSettings { PluginId = pluginId };

        foreach (var (key, value) in values)
        {
            definition.Validation.Values[key] = value;
        }

        return WacsArgumentBuilder.BuildCreateRenewal(TestData.WacsPath, definition).ArgumentValues;
    }

    private static string ValueAfter(IReadOnlyList<string> args, string flag)
    {
        var index = args.ToList().IndexOf(flag);
        Assert.True(index >= 0, $"Expected the command line to contain {flag}.");
        return args[index + 1];
    }

    [Theory]
    [InlineData("gcpdns")]
    [InlineData("godaddy")]
    [InlineData("dnsmadeeasy")]
    [InlineData("digitalocean")]
    [InlineData("linode")]
    [InlineData("dreamhost")]
    public void EachProviderSelectsItsOwnPluginOverDns(string pluginId)
    {
        var args = ArgumentsFor(pluginId);

        Assert.Equal("dns-01", ValueAfter(args, "--validationmode"));
        Assert.Equal(pluginId, ValueAfter(args, "--validation"));
    }

    [Fact]
    public void GoogleCloudDnsTakesAKeyFileAndProject()
    {
        var args = ArgumentsFor("gcpdns",
            ("serviceaccountkey", @"C:\keys\dns.json"),
            ("projectid", "my-project"));

        Assert.Equal(@"C:\keys\dns.json", ValueAfter(args, "--serviceaccountkey"));
        Assert.Equal("my-project", ValueAfter(args, "--projectid"));
    }

    [Fact]
    public void GoDaddyAndDnsMadeEasyBothTakeAKeyAndSecret()
    {
        foreach (var id in new[] { "godaddy", "dnsmadeeasy" })
        {
            var args = ArgumentsFor(id, ("apikey", "k"), ("apisecret", "s"));

            Assert.Equal("k", ValueAfter(args, "--apikey"));
            Assert.Equal("s", ValueAfter(args, "--apisecret"));
        }
    }

    [Fact]
    public void DreamHostTakesOnlyAKey()
    {
        var args = ArgumentsFor("dreamhost", ("apikey", "k"));

        Assert.Equal("k", ValueAfter(args, "--apikey"));
        Assert.DoesNotContain("--apisecret", args);
    }

    [Fact]
    public void DigitalOceanAndLinodeUseTheirOwnTokenFlags()
    {
        Assert.Equal("t", ValueAfter(ArgumentsFor("digitalocean", ("digitaloceanapitoken", "t")), "--digitaloceanapitoken"));
        Assert.Equal("t", ValueAfter(ArgumentsFor("linode", ("apitoken", "t")), "--apitoken"));
    }

    [Theory]
    [InlineData("gcpdns", "serviceaccountkey")]
    [InlineData("godaddy", "apikey")]
    [InlineData("dnsmadeeasy", "apisecret")]
    [InlineData("digitalocean", "digitaloceanapitoken")]
    [InlineData("linode", "apitoken")]
    [InlineData("dreamhost", "apikey")]
    public void EveryCredentialIsRequired(string pluginId, string fieldName)
    {
        var definition = TestData.ValidCloudflareDefinition();
        definition.Validation = new ValidationSettings { PluginId = pluginId };

        foreach (var field in PluginCatalog.Get(pluginId).Fields)
        {
            definition.Validation.Values[field.Name] = "filled";
        }

        Assert.Empty(RenewalValidator.Validate(definition));

        definition.Validation.Values[fieldName] = string.Empty;
        Assert.NotEmpty(RenewalValidator.Validate(definition));
    }

    [Fact]
    public void EveryTokenAndSecretIsMaskedInThePreview()
    {
        foreach (var plugin in PluginCatalog.ValidationPlugins.Where(p => !p.UsesScript))
        {
            var definition = TestData.ValidCloudflareDefinition();
            definition.Validation = new ValidationSettings { PluginId = plugin.Id };

            foreach (var field in plugin.Fields.Where(f => f.Kind != PluginFieldKind.Boolean))
            {
                definition.Validation.Values[field.Name] = "topsecret-" + field.Name;
            }

            var command = WacsArgumentBuilder.BuildCreateRenewal(TestData.WacsPath, definition);
            var shown = command.ToDisplayString();

            foreach (var secret in plugin.Fields.Where(f => f.IsSecret))
            {
                Assert.DoesNotContain("topsecret-" + secret.Name, shown, StringComparison.Ordinal);
            }
        }
    }

    // ------------------------------------------------------------- downloads

    /// <summary>
    /// The one place the plugin id and the release asset disagree. Getting this wrong
    /// makes the installer quietly skip the plugin and the provider never appear.
    /// </summary>
    [Fact]
    public void GoogleCloudDnsShipsUnderADifferentName()
    {
        var plugin = PluginCatalog.Get("gcpdns");

        Assert.Equal("googledns", plugin.EffectiveDownloadName);
        Assert.NotEqual(plugin.Id, plugin.EffectiveDownloadName);
    }

    [Fact]
    public void EveryOtherProviderShipsUnderItsOwnId()
    {
        foreach (var id in new[] { "godaddy", "dnsmadeeasy", "digitalocean", "linode", "dreamhost" })
        {
            Assert.Equal(id, PluginCatalog.Get(id).EffectiveDownloadName);
        }
    }

    [Fact]
    public void TheNewProvidersAreAllSeparateDownloadsOnTheDnsChallenge()
    {
        foreach (var id in new[] { "gcpdns", "godaddy", "dnsmadeeasy", "digitalocean", "linode", "dreamhost" })
        {
            var plugin = PluginCatalog.Get(id);

            Assert.True(plugin.RequiresSeparateDownload);
            Assert.Contains(plugin, PluginCatalog.ForChallenge(PluginCatalog.DnsChallenge));
        }
    }

    /// <summary>
    /// Three providers all contribute --apikey, which is why detection matches the
    /// plugin's own condition line instead.
    /// </summary>
    [Fact]
    public void SharedArgumentsDoNotConfuseDetection()
    {
        const string onlyGoDaddyInstalled = """
            ## GoDaddy
            ``` [--validation godaddy] ```
               --apikey
                 GoDaddy API key.
               --apisecret
                 GoDaddy API secret.
            """;

        var found = WacsInspector.ReadAvailablePlugins(onlyGoDaddyInstalled);

        Assert.Contains("godaddy", found);
        Assert.DoesNotContain("dreamhost", found);
        Assert.DoesNotContain("dnsmadeeasy", found);
    }
}
