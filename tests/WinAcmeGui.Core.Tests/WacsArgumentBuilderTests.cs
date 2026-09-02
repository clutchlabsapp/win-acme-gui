using WinAcmeGui.Core;
using WinAcmeGui.Core.Models;
using WinAcmeGui.Core.Plugins;
using Xunit;

namespace WinAcmeGui.Core.Tests;

public class WacsArgumentBuilderTests
{
    [Fact]
    public void CloudflareProducesTheExpectedCommandLine()
    {
        var command = WacsArgumentBuilder.BuildCreateRenewal(
            TestData.WacsPath, TestData.ValidCloudflareDefinition());

        Assert.Equal(
            new[]
            {
                "--source", "manual",
                "--host", "example.com,www.example.com",
                "--commonname", "example.com",
                "--friendlyname", "Example cert",
                "--accepttos",
                "--emailaddress", "admin@example.com",
                "--validationmode", "dns-01",
                "--validation", "cloudflare",
                "--cloudflareapitoken", "cf-token",
                "--store", "certificatestore",
                "--installation", "none",
                "--verbose",
            },
            command.ArgumentValues);
    }

    [Fact]
    public void Route53WithAccessKeysEmitsBothCredentialFlags()
    {
        var definition = TestData.ValidCloudflareDefinition();
        definition.Validation = new ValidationSettings
        {
            PluginId = PluginCatalog.Route53Id,
            Values =
            {
                ["route53accesskeyid"] = "AKIAEXAMPLE",
                ["route53secretaccesskey"] = "s3cret",
            },
        };

        var args = WacsArgumentBuilder.BuildCreateRenewal(TestData.WacsPath, definition).ArgumentValues;

        Assert.Contains("route53", args);
        Assert.Equal("AKIAEXAMPLE", ValueAfter(args, "--route53accesskeyid"));
        Assert.Equal("s3cret", ValueAfter(args, "--route53secretaccesskey"));
        Assert.DoesNotContain("--route53iamrole", args);
    }

    [Fact]
    public void AzureManagedIdentityEmitsABareFlagAndOmitsTheClientSecret()
    {
        var definition = TestData.ValidCloudflareDefinition();
        definition.Validation = new ValidationSettings
        {
            PluginId = PluginCatalog.AzureId,
            Values =
            {
                ["azuresubscriptionid"] = "sub-1",
                ["azureresourcegroupname"] = "rg-dns",
                ["azureusemsi"] = "true",
            },
        };

        var args = WacsArgumentBuilder.BuildCreateRenewal(TestData.WacsPath, definition).ArgumentValues;

        Assert.Contains("--azureusemsi", args);
        Assert.DoesNotContain("--azuresecret", args);
        Assert.DoesNotContain("--azurehostedzone", args);

        // A boolean flag must not be followed by a value.
        var index = args.ToList().IndexOf("--azureusemsi");
        Assert.StartsWith("--", args[index + 1]);
    }

    [Fact]
    public void FalseBooleanFieldsAreNotEmitted()
    {
        var definition = TestData.ValidCloudflareDefinition();
        definition.Validation = new ValidationSettings
        {
            PluginId = PluginCatalog.AzureId,
            Values =
            {
                ["azuresubscriptionid"] = "sub-1",
                ["azureresourcegroupname"] = "rg-dns",
                ["azureusemsi"] = "false",
                ["azuretenantid"] = "tenant",
                ["azureclientid"] = "client",
                ["azuresecret"] = "shhh",
            },
        };

        var args = WacsArgumentBuilder.BuildCreateRenewal(TestData.WacsPath, definition).ArgumentValues;

        Assert.DoesNotContain("--azureusemsi", args);
        Assert.Equal("shhh", ValueAfter(args, "--azuresecret"));
    }

    [Fact]
    public void BlankFieldsNeverProduceADanglingFlag()
    {
        var definition = TestData.ValidCloudflareDefinition();
        definition.Certificate.FriendlyName = "   ";
        definition.Certificate.CommonName = string.Empty;
        definition.Store.StoreName = string.Empty;

        var args = WacsArgumentBuilder.BuildCreateRenewal(TestData.WacsPath, definition).ArgumentValues;

        Assert.DoesNotContain("--friendlyname", args);
        Assert.DoesNotContain("--commonname", args);
        Assert.DoesNotContain("--certificatestore", args);
        Assert.DoesNotContain(string.Empty, args);
    }

    [Fact]
    public void TestModeAddsTestAndCloseOnFinish()
    {
        var definition = TestData.ValidCloudflareDefinition();
        definition.Account.UseTestServer = true;

        var args = WacsArgumentBuilder.BuildCreateRenewal(TestData.WacsPath, definition).ArgumentValues;

        Assert.Contains("--test", args);
        Assert.Contains("--closeonfinish", args);
    }

    [Fact]
    public void CloseOnFinishIsOnlyEmittedAlongsideTest()
    {
        var args = WacsArgumentBuilder.BuildCreateRenewal(
            TestData.WacsPath, TestData.ValidCloudflareDefinition()).ArgumentValues;

        Assert.DoesNotContain("--test", args);
        Assert.DoesNotContain("--closeonfinish", args);
    }

    [Fact]
    public void KeepExistingAndCustomStoreNameAreEmitted()
    {
        var definition = TestData.ValidCloudflareDefinition();
        definition.Store.StoreName = "My";
        definition.Store.KeepExisting = true;

        var args = WacsArgumentBuilder.BuildCreateRenewal(TestData.WacsPath, definition).ArgumentValues;

        Assert.Equal("My", ValueAfter(args, "--certificatestore"));
        Assert.Contains("--keepexisting", args);
    }

    [Fact]
    public void UnknownValidationPluginEmitsNoValidationFlags()
    {
        var definition = TestData.ValidCloudflareDefinition();
        definition.Validation = new ValidationSettings { PluginId = "not-a-plugin" };

        var args = WacsArgumentBuilder.BuildCreateRenewal(TestData.WacsPath, definition).ArgumentValues;

        Assert.DoesNotContain("--validation", args);
        Assert.DoesNotContain("--validationmode", args);
    }

    [Fact]
    public void DisplayStringRedactsSecretsByDefault()
    {
        var command = WacsArgumentBuilder.BuildCreateRenewal(
            TestData.WacsPath, TestData.ValidCloudflareDefinition());

        var redacted = command.ToDisplayString();

        Assert.DoesNotContain("cf-token", redacted);
        Assert.Contains(WacsCommand.RedactedPlaceholder, redacted);
        Assert.Contains("--cloudflareapitoken", redacted);
        Assert.True(command.ContainsSecrets);
    }

    [Fact]
    public void DisplayStringCanIncludeSecretsAndQuotesWhatNeedsIt()
    {
        var command = WacsArgumentBuilder.BuildCreateRenewal(
            TestData.WacsPath, TestData.ValidCloudflareDefinition());

        var full = command.ToDisplayString(redactSecrets: false);

        Assert.Contains("cf-token", full);
        Assert.Contains("\"Example cert\"", full);
        Assert.StartsWith("\"C:\\win-acme\\wacs.exe\"", full);
    }

    [Fact]
    public void NonSecretArgumentsAreNeverRedacted()
    {
        var command = WacsArgumentBuilder.BuildCreateRenewal(
            TestData.WacsPath, TestData.ValidCloudflareDefinition());

        var redacted = command.ToDisplayString();

        Assert.Contains("admin@example.com", redacted);
        Assert.Contains("example.com,www.example.com", redacted);
    }

    [Fact]
    public void ListAndVersionAreSingleArgumentCommands()
    {
        Assert.Equal(new[] { "--list" }, WacsArgumentBuilder.BuildList(TestData.WacsPath).ArgumentValues);
        Assert.Equal(new[] { "--version" }, WacsArgumentBuilder.BuildVersion(TestData.WacsPath).ArgumentValues);
    }

    [Fact]
    public void RenewNowTargetsOneRenewalAndCanForce()
    {
        var args = WacsArgumentBuilder
            .BuildRenewNow(TestData.WacsPath, "abc123", force: true, useTestServer: true)
            .ArgumentValues;

        Assert.Equal(
            new[] { "--renew", "--id", "abc123", "--force", "--test", "--closeonfinish", "--verbose" },
            args);
    }

    [Fact]
    public void RenewNowFallsBackToTheFriendlyNameWhenThereIsNoId()
    {
        var args = WacsArgumentBuilder
            .BuildRenewNow(TestData.WacsPath, friendlyName: "Example cert", force: true)
            .ArgumentValues;

        Assert.Equal(
            new[] { "--renew", "--friendlyname", "Example cert", "--force", "--verbose" },
            args);
    }

    [Fact]
    public void AnIdWinsOverAFriendlyName()
    {
        var args = WacsArgumentBuilder
            .BuildRenewNow(TestData.WacsPath, renewalId: "abc123", friendlyName: "Example cert")
            .ArgumentValues;

        Assert.Contains("--id", args);
        Assert.DoesNotContain("--friendlyname", args);
    }

    [Fact]
    public void RenewNowWithoutAnIdRenewsEverythingThatIsDue()
    {
        var args = WacsArgumentBuilder.BuildRenewNow(TestData.WacsPath).ArgumentValues;

        Assert.Equal(new[] { "--renew", "--verbose" }, args);
    }

    private static string ValueAfter(IReadOnlyList<string> args, string flag)
    {
        var index = args.ToList().IndexOf(flag);
        Assert.True(index >= 0, $"Expected the command line to contain {flag}.");
        Assert.True(index + 1 < args.Count, $"{flag} was the last argument, with no value after it.");
        return args[index + 1];
    }
}
