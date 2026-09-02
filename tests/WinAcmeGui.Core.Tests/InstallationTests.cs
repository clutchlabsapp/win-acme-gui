using WinAcmeGui.Core;
using WinAcmeGui.Core.Models;
using WinAcmeGui.Core.Plugins;
using Xunit;

namespace WinAcmeGui.Core.Tests;

public class InstallationTests
{
    private static IReadOnlyList<string> ArgumentsFor(InstallationSettings installation)
    {
        var definition = TestData.ValidCloudflareDefinition();
        definition.Installation = installation;
        return WacsArgumentBuilder.BuildCreateRenewal(TestData.WacsPath, definition).ArgumentValues;
    }

    private static string ValueAfter(IReadOnlyList<string> args, string flag)
    {
        var index = args.ToList().IndexOf(flag);
        Assert.True(index >= 0, $"Expected the command line to contain {flag}.");
        return args[index + 1];
    }

    [Fact]
    public void NothingConfiguredStillSaysInstallationNone()
    {
        var args = ArgumentsFor(new InstallationSettings());

        Assert.Equal("none", ValueAfter(args, "--installation"));
        Assert.DoesNotContain("--script", args);
        Assert.DoesNotContain("--installationsiteid", args);
    }

    [Fact]
    public void IisOnlyEmitsTheIisPluginAndItsOptions()
    {
        var args = ArgumentsFor(new InstallationSettings
        {
            UpdateIisBindings = true,
            IisSiteId = "3",
            SslPort = "8443",
            SslIpAddress = "10.0.0.5",
        });

        Assert.Equal("iis", ValueAfter(args, "--installation"));
        Assert.Equal("3", ValueAfter(args, "--installationsiteid"));
        Assert.Equal("8443", ValueAfter(args, "--sslport"));
        Assert.Equal("10.0.0.5", ValueAfter(args, "--sslipaddress"));
        Assert.DoesNotContain("--script", args);
    }

    [Fact]
    public void BlankIisOptionsAreOmitted()
    {
        var args = ArgumentsFor(new InstallationSettings { UpdateIisBindings = true });

        Assert.Equal("iis", ValueAfter(args, "--installation"));
        Assert.DoesNotContain("--installationsiteid", args);
        Assert.DoesNotContain("--sslport", args);
        Assert.DoesNotContain("--sslipaddress", args);
    }

    [Theory]
    [InlineData("rdlistener", "ImportRDListener.ps1")]
    [InlineData("rdgateway", "ImportRDGateway.ps1")]
    [InlineData("rds", "ImportRDS.ps1")]
    [InlineData("exchange", "ImportExchange.ps1")]
    public void ABundledScriptResolvesToWinAcmesOwnScriptsFolder(string presetId, string fileName)
    {
        var args = ArgumentsFor(new InstallationSettings { ScriptPresetId = presetId });

        Assert.Equal("script", ValueAfter(args, "--installation"));

        var script = ValueAfter(args, "--script");
        Assert.EndsWith(fileName, script, StringComparison.Ordinal);
        Assert.Contains("Scripts", script, StringComparison.Ordinal);
        Assert.StartsWith(@"C:\win-acme", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRdpScriptGetsTheCertificateThumbprint()
    {
        var args = ArgumentsFor(new InstallationSettings { ScriptPresetId = "rdlistener" });

        Assert.Equal("{CertThumbprint}", ValueAfter(args, "--scriptparameters"));
    }

    [Fact]
    public void IisAndAScriptCanBeCombined()
    {
        var args = ArgumentsFor(new InstallationSettings
        {
            UpdateIisBindings = true,
            ScriptPresetId = "rds",
        });

        Assert.Equal("iis,script", ValueAfter(args, "--installation"));
        Assert.Contains("--script", args);
    }

    [Fact]
    public void ACustomScriptUsesTheGivenPathAndParameters()
    {
        var args = ArgumentsFor(new InstallationSettings
        {
            ScriptPresetId = InstallationPresets.CustomId,
            ScriptPath = @"C:\scripts\deploy.ps1",
            ScriptParameters = "-Thumbprint {CertThumbprint} -Path \"{CacheFile}\"",
        });

        Assert.Equal(@"C:\scripts\deploy.ps1", ValueAfter(args, "--script"));
        Assert.Equal("-Thumbprint {CertThumbprint} -Path \"{CacheFile}\"", ValueAfter(args, "--scriptparameters"));
    }

    [Fact]
    public void TypedParametersOverrideThePresetDefault()
    {
        var args = ArgumentsFor(new InstallationSettings
        {
            ScriptPresetId = "exchange",
            ScriptParameters = "{CertThumbprint} IIS,SMTP,IMAP 0",
        });

        Assert.Equal("{CertThumbprint} IIS,SMTP,IMAP 0", ValueAfter(args, "--scriptparameters"));
    }

    [Fact]
    public void AnUnknownPresetFallsBackToNoScript()
    {
        var args = ArgumentsFor(new InstallationSettings { ScriptPresetId = "not-a-preset" });

        Assert.Equal("none", ValueAfter(args, "--installation"));
        Assert.DoesNotContain("--script", args);
    }

    [Fact]
    public void EveryPresetHasAUniqueIdAndTheBundledOnesNameAScript()
    {
        var ids = InstallationPresets.Scripts.Select(p => p.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var preset in InstallationPresets.Scripts.Where(p => !p.IsNone && !p.IsCustom))
        {
            Assert.EndsWith(".ps1", preset.ScriptFileName, StringComparison.Ordinal);
            Assert.NotEmpty(preset.DefaultParameters);
        }
    }

    // ------------------------------------------------------------ validation

    private static IReadOnlyList<string> ProblemsFor(InstallationSettings installation, string storeName = "")
    {
        var definition = TestData.ValidCloudflareDefinition();
        definition.Installation = installation;
        definition.Store.StoreName = storeName;
        return RenewalValidator.Validate(definition);
    }

    [Fact]
    public void TheRdpScriptsRequireTheMyStore()
    {
        Assert.Contains(
            ProblemsFor(new InstallationSettings { ScriptPresetId = "rdlistener" }, storeName: "WebHosting"),
            problem => problem.Contains(@"LocalMachine\My", StringComparison.Ordinal));

        Assert.Empty(ProblemsFor(new InstallationSettings { ScriptPresetId = "rdlistener" }, storeName: "My"));
    }

    [Fact]
    public void ABlankStoreIsAlsoWrongForTheRdpScripts()
    {
        // Blank means WebHosting, which the RD scripts never look in.
        Assert.NotEmpty(ProblemsFor(new InstallationSettings { ScriptPresetId = "rds" }));
    }

    [Fact]
    public void ACustomScriptWithoutAPathIsReported()
    {
        Assert.Contains(
            ProblemsFor(new InstallationSettings { ScriptPresetId = InstallationPresets.CustomId }),
            problem => problem.Contains("script to run", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("0")]
    [InlineData("-1")]
    public void IisPortAndSiteIdMustBePositiveNumbers(string value)
    {
        Assert.NotEmpty(ProblemsFor(new InstallationSettings { UpdateIisBindings = true, SslPort = value }));
        Assert.NotEmpty(ProblemsFor(new InstallationSettings { UpdateIisBindings = true, IisSiteId = value }));
    }

    [Fact]
    public void BlankIisNumbersAreAcceptedBecauseWinAcmeHasDefaults()
    {
        Assert.Empty(ProblemsFor(new InstallationSettings { UpdateIisBindings = true }));
    }
}
