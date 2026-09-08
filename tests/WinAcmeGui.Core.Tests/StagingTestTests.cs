using WinAcmeGui.Core;
using WinAcmeGui.Core.Models;
using WinAcmeGui.Core.Plugins;
using Xunit;

namespace WinAcmeGui.Core.Tests;

public class StagingTestTests
{
    /// <summary>A definition with everything a dry run must deliberately drop.</summary>
    private static RenewalDefinition FullyConfigured()
    {
        var definition = TestData.ValidCloudflareDefinition();

        definition.Account.UseTestServer = false;
        definition.Store.StoreName = "My";
        definition.Store.KeepExisting = true;
        definition.Installation = new InstallationSettings
        {
            UpdateIisBindings = true,
            IisSiteId = "3",
            ScriptPresetId = "rdlistener",
        };

        return definition;
    }

    private static IReadOnlyList<string> StagingArgs(RenewalDefinition definition) =>
        WacsArgumentBuilder.BuildStagingTest(TestData.WacsPath, definition).ArgumentValues;

    private static string ValueAfter(IReadOnlyList<string> args, string flag)
    {
        var index = args.ToList().IndexOf(flag);
        Assert.True(index >= 0, $"Expected the command line to contain {flag}.");
        return args[index + 1];
    }

    [Fact]
    public void AlwaysUsesTheStagingEndpoint()
    {
        var args = StagingArgs(FullyConfigured());

        Assert.Contains("--test", args);
        Assert.Contains("--closeonfinish", args);
    }

    [Fact]
    public void NeverRunsThePostRenewalHooks()
    {
        // Binding a staging certificate to IIS, or importing it into the RDP listener,
        // would break the very thing this is meant to prove is safe.
        var args = StagingArgs(FullyConfigured());

        Assert.Equal("none", ValueAfter(args, "--installation"));
        Assert.DoesNotContain("--script", args);
        Assert.DoesNotContain("--installationsiteid", args);
    }

    [Fact]
    public void WritesTheCertificateNowhereAtAll()
    {
        var args = StagingArgs(FullyConfigured());

        Assert.Equal("none", ValueAfter(args, "--store"));
        Assert.DoesNotContain("--certificatestore", args);
        Assert.DoesNotContain("--keepexisting", args);

        // The earlier version wrote a .pfx to a temp folder it never created, which
        // failed on a real machine: win-acme requires the directory to exist.
        Assert.DoesNotContain("--pfxfilepath", args);
        Assert.DoesNotContain("--pemfilespath", args);
    }

    [Fact]
    public void KeepsTheValidationSettingsBeingTested()
    {
        var args = StagingArgs(FullyConfigured());

        Assert.Equal("cloudflare", ValueAfter(args, "--validation"));
        Assert.Equal("cf-token", ValueAfter(args, "--cloudflareapitoken"));
        Assert.Equal("example.com,www.example.com", ValueAfter(args, "--host"));
    }

    [Fact]
    public void RunsUnderItsOwnNameSoTheRealRenewalSurvives()
    {
        var definition = FullyConfigured();
        var args = StagingArgs(definition);

        var name = ValueAfter(args, "--friendlyname");

        Assert.NotEqual(definition.Certificate.FriendlyName, name);
        Assert.EndsWith(WacsArgumentBuilder.StagingNameSuffix, name, StringComparison.Ordinal);
        Assert.StartsWith("Example cert", name, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNameIsTheSameEveryTimeSoCleanupCanFindIt()
    {
        var definition = FullyConfigured();

        Assert.Equal(
            WacsArgumentBuilder.StagingFriendlyName(definition),
            WacsArgumentBuilder.StagingFriendlyName(definition));

        Assert.Equal(
            WacsArgumentBuilder.StagingFriendlyName(definition),
            ValueAfter(StagingArgs(definition), "--friendlyname"));
    }

    [Fact]
    public void FallsBackToTheFirstHostWhenThereIsNoFriendlyName()
    {
        var definition = FullyConfigured();
        definition.Certificate.FriendlyName = "   ";

        Assert.StartsWith("example.com", WacsArgumentBuilder.StagingFriendlyName(definition), StringComparison.Ordinal);
    }

    [Fact]
    public void WorksForAScriptDrivenProviderToo()
    {
        var definition = FullyConfigured();
        definition.Validation = new ValidationSettings
        {
            PluginId = PluginCatalog.NamecheapId,
            Values =
            {
                ["namecheapexe"] = @"C:\win-acme\NameCheap.exe",
                ["apikey"] = "secret",
            },
        };

        var args = StagingArgs(definition);

        Assert.Equal("script", ValueAfter(args, "--validation"));
        Assert.Equal(@"C:\win-acme\NameCheap.exe", ValueAfter(args, "--dnsscript"));
        Assert.DoesNotContain("secret", args);
    }

    [Fact]
    public void TheRealCommandIsLeftAlone()
    {
        var definition = FullyConfigured();
        var real = WacsArgumentBuilder.BuildCreateRenewal(TestData.WacsPath, definition).ArgumentValues;

        Assert.DoesNotContain("--test", real);
        Assert.Equal("certificatestore", ValueAfter(real, "--store"));
        Assert.Equal("iis,script", ValueAfter(real, "--installation"));
        Assert.Equal("Example cert", ValueAfter(real, "--friendlyname"));

        // Building the dry run must not have mutated the definition it was given.
        Assert.False(definition.Account.UseTestServer);
        Assert.Equal("Example cert", definition.Certificate.FriendlyName);
        Assert.True(definition.Installation.UpdateIisBindings);
    }

    // ------------------------------------------------------------- cleanup

    [Fact]
    public void CancelTargetsOneRenewalByName()
    {
        var args = WacsArgumentBuilder
            .BuildCancelRenewal(TestData.WacsPath, "Example cert [staging test]")
            .ArgumentValues;

        Assert.Equal(
            new[] { "--cancel", "--friendlyname", "Example cert [staging test]" },
            args);
    }

    [Fact]
    public void CancelTrimsAndRefusesAnEmptyName()
    {
        Assert.Equal(
            "a [staging test]",
            WacsArgumentBuilder.BuildCancelRenewal(TestData.WacsPath, "  a [staging test]  ").ArgumentValues[2]);

        Assert.Throws<ArgumentException>(() => WacsArgumentBuilder.BuildCancelRenewal(TestData.WacsPath, "  "));
    }

    [Fact]
    public void CleanupNeverTargetsTheRealRenewal()
    {
        var definition = FullyConfigured();
        var name = WacsArgumentBuilder.StagingFriendlyName(definition);

        var args = WacsArgumentBuilder.BuildCancelRenewal(TestData.WacsPath, name).ArgumentValues;

        Assert.Equal(name, args[2]);
        Assert.DoesNotContain(definition.Certificate.FriendlyName, args);
    }
}
