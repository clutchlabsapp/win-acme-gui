using WinAcmeGui.Core;
using WinAcmeGui.Core.Plugins;
using Xunit;

namespace WinAcmeGui.Core.Tests;

public class WacsInspectorTests
{
    private const string TrimmedVersionOutput = """
         A simple Windows ACMEv2 client (WACS)
         Software version 2.2.9.1701 (release, trimmed, standalone, 64-bit)
        """;

    private const string PluggableVersionOutput = """
         A simple Windows ACMEv2 client (WACS)
         Software version 2.2.9.1701 (release, pluggable, standalone, 64-bit)
        """;

    [Fact]
    public void ReadsTheVersionAndSpotsATrimmedBuild()
    {
        var installation = WacsInspector.ReadVersion(TestData.WacsPath, TrimmedVersionOutput);

        Assert.NotNull(installation);
        Assert.Equal("2.2.9.1701", installation.Version);
        Assert.True(installation.IsTrimmed);
        Assert.False(installation.SupportsPlugins);
        Assert.Equal("win-acme 2.2.9.1701 (trimmed)", installation.Description);
    }

    [Fact]
    public void SpotsAPluggableBuild()
    {
        var installation = WacsInspector.ReadVersion(TestData.WacsPath, PluggableVersionOutput);

        Assert.NotNull(installation);
        Assert.False(installation.IsTrimmed);
        Assert.True(installation.SupportsPlugins);
        Assert.Equal("win-acme 2.2.9.1701 (pluggable)", installation.Description);
    }

    [Fact]
    public void UnrecognisableVersionOutputGivesNothing()
    {
        Assert.Null(WacsInspector.ReadVersion(TestData.WacsPath, null));
        Assert.Null(WacsInspector.ReadVersion(TestData.WacsPath, string.Empty));
        Assert.Null(WacsInspector.ReadVersion(TestData.WacsPath, "command not recognised"));
    }

    [Fact]
    public void DetectsInstalledPluginsFromTheirCommandLineArguments()
    {
        const string help = """
            ## Cloudflare
            ``` [--validation cloudflare] ```
               --cloudflareapitoken
                 API Token for Cloudflare.

            ## Route53
            ``` [--validation route53] ```
               --route53iamrole
                 AWS IAM role for the current EC2 instance.
            """;

        var plugins = WacsInspector.ReadAvailablePlugins(help);

        Assert.Contains(PluginCatalog.CloudflareId, plugins);
        Assert.Contains(PluginCatalog.Route53Id, plugins);
        Assert.DoesNotContain(PluginCatalog.AzureId, plugins);
    }

    [Fact]
    public void TheKeyVaultStoreIsNotMistakenForTheAzureDnsPlugin()
    {
        // The KeyVault store plugin contributes --azuretenantid, --azureclientid and
        // --azuresecret too, so matching arguments would give a false positive. Its
        // condition is --store keyvault, not --validation azure.
        const string keyVaultOnlyHelp = """
            ## Azure KeyVault
            ``` [--store keyvault] ```
               --vaultname
                 The name of the vault
               --azuretenantid
                 Directory/tenant identifier.
               --azureclientid
                 Application/client identifier.
               --azuresecret
                 Client secret.
            """;

        Assert.DoesNotContain(PluginCatalog.AzureId, WacsInspector.ReadAvailablePlugins(keyVaultOnlyHelp));
    }

    [Fact]
    public void DetectsAzureWhenItsOwnArgumentIsPresent()
    {
        const string help = """
            ## Azure
            ``` [--validation azure] ```
               --azuresubscriptionid
                 Subscription ID to login into Microsoft Azure DNS.
            """;

        Assert.Contains(PluginCatalog.AzureId, WacsInspector.ReadAvailablePlugins(help));
    }

    [Fact]
    public void NoPluginsAreReportedForEmptyHelpOutput()
    {
        Assert.Empty(WacsInspector.ReadAvailablePlugins(null));
        Assert.Empty(WacsInspector.ReadAvailablePlugins("   "));
    }

    [Fact]
    public void EveryCatalogPluginIsDetectedByItsOwnCondition()
    {
        var conditions = PluginCatalog.ValidationPlugins.Select(p => p.HelpCondition).ToList();

        Assert.Equal(conditions.Count, conditions.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(conditions, c => Assert.StartsWith("--validation ", c, StringComparison.Ordinal));
    }

    [Fact]
    public void HasPluginIsCaseInsensitive()
    {
        var installation = new WacsInstallation(TestData.WacsPath, "2.2.9.1701", IsTrimmed: false)
        {
            AvailableValidationPlugins = ["cloudflare"],
        };

        Assert.True(installation.HasPlugin("CloudFlare"));
        Assert.False(installation.HasPlugin("route53"));
    }

    [Fact]
    public async Task AMissingExecutableIsReportedAsNotInstalled()
    {
        var inspector = new WacsInspector(new WacsRunner());

        Assert.Null(await inspector.InspectAsync(null));
        Assert.Null(await inspector.InspectAsync("   "));
        Assert.Null(await inspector.InspectAsync(Path.Combine(Path.GetTempPath(), "no-such-wacs.exe")));
    }
}
