using System.Text.Json;
using WinAcmeGui.Core;
using WinAcmeGui.Core.Models;
using WinAcmeGui.Core.Plugins;
using Xunit;

namespace WinAcmeGui.Core.Tests;

public class NamecheapTests : IDisposable
{
    private const string HelperPath = @"C:\win-acme\NameCheap.exe";

    private readonly string _workspace = Path.Combine(
        Path.GetTempPath(), "win-acme-gui-namecheap-" + Guid.NewGuid().ToString("N"));

    public NamecheapTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workspace, recursive: true);
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }

    private static RenewalDefinition Definition(
        string executable = HelperPath,
        string apiKey = "nc-secret-key",
        string userName = "someone",
        string apiUserName = "someone",
        string clientIp = "203.0.113.10")
    {
        var definition = TestData.ValidCloudflareDefinition();

        definition.Validation = new ValidationSettings
        {
            PluginId = PluginCatalog.NamecheapId,
            Values =
            {
                ["namecheapexe"] = executable,
                ["apikey"] = apiKey,
                ["username"] = userName,
                ["apiusername"] = apiUserName,
                ["clientip"] = clientIp,
            },
        };

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

    // ------------------------------------------------------- argument building

    [Fact]
    public void UsesWinAcmesBuiltInScriptPluginNotAPluginNamedNamecheap()
    {
        var args = ArgumentsFor(Definition());

        Assert.Equal("script", ValueAfter(args, "--validation"));
        Assert.DoesNotContain("namecheap", args);
        Assert.Equal("dns-01", ValueAfter(args, "--validationmode"));
    }

    [Fact]
    public void PassesTheHelperAndTheArgumentsItExpects()
    {
        var args = ArgumentsFor(Definition());

        Assert.Equal(HelperPath, ValueAfter(args, "--dnsscript"));
        Assert.Equal("create {ZoneName} {NodeName} {Token}", ValueAfter(args, "--dnscreatescriptarguments"));
        Assert.Equal("delete {ZoneName} {NodeName} {Token}", ValueAfter(args, "--dnsdeletescriptarguments"));
    }

    /// <summary>
    /// The whole reason this provider reads a config file: the Namecheap API key must
    /// never be visible in the process arguments of a running wacs.exe.
    /// </summary>
    [Fact]
    public void CredentialsNeverReachTheCommandLine()
    {
        var command = WacsArgumentBuilder.BuildCreateRenewal(TestData.WacsPath, Definition());

        Assert.DoesNotContain("nc-secret-key", command.ArgumentValues);
        Assert.DoesNotContain("203.0.113.10", command.ArgumentValues);
        Assert.DoesNotContain("--apikey", command.ArgumentValues);
        Assert.DoesNotContain("--clientip", command.ArgumentValues);
        Assert.DoesNotContain("--username", command.ArgumentValues);
        Assert.DoesNotContain("--apiusername", command.ArgumentValues);

        var rendered = command.ToDisplayString(redactSecrets: false);
        Assert.DoesNotContain("nc-secret-key", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void ABlankHelperPathProducesNoDanglingFlag()
    {
        var args = ArgumentsFor(Definition(executable: "   "));

        Assert.DoesNotContain("--dnsscript", args);
        Assert.DoesNotContain("--dnscreatescriptarguments", args);
        Assert.Equal("script", ValueAfter(args, "--validation"));
    }

    [Fact]
    public void ParallelismIsLeftSerialBecauseTheHelperRewritesTheWholeRecordSet()
    {
        Assert.DoesNotContain("--dnsscriptparallelism", ArgumentsFor(Definition()));
    }

    // ------------------------------------------------------------ the catalog

    [Fact]
    public void TheProviderNeedsNoSeparateDownloadBecauseScriptIsBuiltIn()
    {
        var plugin = PluginCatalog.Get(PluginCatalog.NamecheapId);

        Assert.False(plugin.RequiresSeparateDownload);
        Assert.True(plugin.UsesScript);
        Assert.Equal("--dnsscript", plugin.EffectiveDetectionFlag);
    }

    [Fact]
    public void OnlyTheHelperPathGoesOnTheCommandLine()
    {
        var plugin = PluginCatalog.Get(PluginCatalog.NamecheapId);

        Assert.Equal(
            new[] { "namecheapexe" },
            plugin.Fields.Where(f => !f.IsExternal).Select(f => f.Name));

        Assert.All(plugin.Fields.Where(f => f.IsExternal), f => Assert.True(f.IsExternal));
    }

    [Fact]
    public void TheOtherProvidersAreUnaffected()
    {
        foreach (var id in new[] { PluginCatalog.CloudflareId, PluginCatalog.Route53Id, PluginCatalog.AzureId })
        {
            var plugin = PluginCatalog.Get(id);

            Assert.True(plugin.RequiresSeparateDownload);
            Assert.False(plugin.UsesScript);
            Assert.All(plugin.Fields, f => Assert.False(f.IsExternal));
        }
    }

    [Fact]
    public void CloudflareStillEmitsItsOwnPluginAndFlags()
    {
        var args = ArgumentsFor(TestData.ValidCloudflareDefinition());

        Assert.Equal("cloudflare", ValueAfter(args, "--validation"));
        Assert.Equal("cf-token", ValueAfter(args, "--cloudflareapitoken"));
        Assert.DoesNotContain("--dnsscript", args);
    }

    // ---------------------------------------------------------- validation

    [Fact]
    public void AFullyConfiguredProviderPassesValidation()
    {
        Assert.Empty(RenewalValidator.Validate(Definition()));
    }

    [Theory]
    [InlineData("namecheapexe")]
    [InlineData("apikey")]
    [InlineData("username")]
    [InlineData("apiusername")]
    [InlineData("clientip")]
    public void EveryValueIsRequired(string fieldName)
    {
        var definition = Definition();
        definition.Validation.Values[fieldName] = string.Empty;

        Assert.NotEmpty(RenewalValidator.Validate(definition));
    }

    // ------------------------------------------------------- settings file

    private string WriteSettings(string json)
    {
        var exe = Path.Combine(_workspace, "NameCheap.exe");
        File.WriteAllText(Path.Combine(_workspace, "appsettings.json"), json);
        return exe;
    }

    [Fact]
    public void TheSettingsFileSitsNextToTheHelper()
    {
        Assert.Equal(
            Path.Combine(@"C:\win-acme", "appsettings.json"),
            NamecheapSettingsFile.ExpectedPath(HelperPath));

        Assert.Equal(string.Empty, NamecheapSettingsFile.ExpectedPath(null));
        Assert.Equal(string.Empty, NamecheapSettingsFile.ExpectedPath("  "));
    }

    [Fact]
    public void ReadsTheFileUpstreamShips()
    {
        // Upstream writes ClientIP but reads ClientIp; both must work.
        var exe = WriteSettings("""
        {
            "NameCheap": {
                "ApiKey": "abc123",
                "UserName": "argentini",
                "ApiUserName": "argentini",
                "ClientIP": "172.217.1.206"
            }
        }
        """);

        var credentials = NamecheapSettingsFile.Read(exe);

        Assert.Equal("abc123", credentials.ApiKey);
        Assert.Equal("argentini", credentials.UserName);
        Assert.Equal("argentini", credentials.ApiUserName);
        Assert.Equal("172.217.1.206", credentials.ClientIp);
        Assert.True(credentials.IsComplete);
    }

    [Fact]
    public void KeyCasingDoesNotMatterOnRead()
    {
        var exe = WriteSettings("""{"namecheap":{"apikey":"k","username":"u","apiusername":"a","clientip":"1.2.3.4"}}""");

        Assert.Equal("k", NamecheapSettingsFile.Read(exe).ApiKey);
        Assert.Equal("1.2.3.4", NamecheapSettingsFile.Read(exe).ClientIp);
    }

    [Fact]
    public void AMissingOrBrokenFileReadsAsEmptyRatherThanThrowing()
    {
        var absent = Path.Combine(_workspace, "nowhere", "NameCheap.exe");
        Assert.Equal(NamecheapCredentials.Empty, NamecheapSettingsFile.Read(absent));

        Assert.Equal(NamecheapCredentials.Empty, NamecheapSettingsFile.Read(WriteSettings("{ not json")));
        Assert.Equal(NamecheapCredentials.Empty, NamecheapSettingsFile.Read(WriteSettings("[]")));
        Assert.Equal(NamecheapCredentials.Empty, NamecheapSettingsFile.Read(WriteSettings("""{"Other":{}}""")));
    }

    [Fact]
    public void AValueOfTheWrongTypeIsTreatedAsAbsent()
    {
        var exe = WriteSettings("""{"NameCheap":{"ApiKey":42,"UserName":"u","ApiUserName":"a","ClientIP":"1.2.3.4"}}""");

        var credentials = NamecheapSettingsFile.Read(exe);

        Assert.Equal(string.Empty, credentials.ApiKey);
        Assert.Equal("u", credentials.UserName);
        Assert.False(credentials.IsComplete);
    }

    [Fact]
    public void WritingThenReadingRoundTrips()
    {
        var exe = Path.Combine(_workspace, "NameCheap.exe");
        var credentials = new NamecheapCredentials("key", "user", "apiuser", "198.51.100.7");

        NamecheapSettingsFile.Write(exe, credentials);

        Assert.Equal(credentials, NamecheapSettingsFile.Read(exe));
    }

    [Fact]
    public void WritingKeepsUnrelatedKeys()
    {
        var exe = WriteSettings("""
        {
            "Logging": { "LogLevel": { "Default": "Information" } },
            "NameCheap": { "ApiKey": "old", "UserName": "old", "ApiUserName": "old", "ClientIP": "0.0.0.0" }
        }
        """);

        NamecheapSettingsFile.Write(exe, new NamecheapCredentials("new", "u", "a", "1.1.1.1"));

        using var document = JsonDocument.Parse(File.ReadAllText(NamecheapSettingsFile.ExpectedPath(exe)));
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("Logging", out _));
        Assert.Equal("new", root.GetProperty("NameCheap").GetProperty("ApiKey").GetString());
    }

    [Fact]
    public void WritingCreatesTheFileWhenThereIsNoneYet()
    {
        var exe = Path.Combine(_workspace, "fresh", "NameCheap.exe");

        NamecheapSettingsFile.Write(exe, new NamecheapCredentials("k", "u", "a", "1.2.3.4"));

        Assert.True(File.Exists(NamecheapSettingsFile.ExpectedPath(exe)));
        Assert.Equal("k", NamecheapSettingsFile.Read(exe).ApiKey);
    }

    [Fact]
    public void MissingValuesAreNamed()
    {
        var credentials = new NamecheapCredentials(string.Empty, "u", string.Empty, "  ");

        Assert.False(credentials.IsComplete);
        Assert.Equal(new[] { "API key", "API username", "whitelisted IP" }, credentials.MissingValues);
    }
}
