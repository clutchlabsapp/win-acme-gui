using WinAcmeGui.Core;
using WinAcmeGui.Core.Models;
using WinAcmeGui.Core.Plugins;
using Xunit;

namespace WinAcmeGui.Core.Tests;

public class RdpScriptTests : IDisposable
{
    private readonly string _workspace = Path.Combine(
        Path.GetTempPath(), "win-acme-gui-rdp-" + Guid.NewGuid().ToString("N"));

    public RdpScriptTests() => Directory.CreateDirectory(_workspace);

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

    private static IReadOnlyList<string> ArgumentsFor(string presetId)
    {
        var definition = TestData.ValidCloudflareDefinition();
        definition.Installation = new InstallationSettings { ScriptPresetId = presetId };

        return WacsArgumentBuilder.BuildCreateRenewal(TestData.WacsPath, definition).ArgumentValues;
    }

    private static string ValueAfter(IReadOnlyList<string> args, string flag)
    {
        var index = args.ToList().IndexOf(flag);
        Assert.True(index >= 0, $"Expected the command line to contain {flag}.");
        return args[index + 1];
    }

    // ------------------------------------------------------- the shipped script

    [Fact]
    public void TheScriptIsEmbeddedInTheAssembly()
    {
        var script = InstallScriptWriter.ReadScript(InstallScriptWriter.RdpListenerScriptName);

        Assert.Contains("param(", script, StringComparison.Ordinal);
        Assert.Contains("$Thumbprint", script, StringComparison.Ordinal);
        Assert.Contains("$CacheFile", script, StringComparison.Ordinal);
        Assert.Contains("$CachePassword", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// The reason this script exists: the bundled one swallows its errors and exits 0,
    /// so win-acme records a renewal that never bound as a success.
    /// </summary>
    [Fact]
    public void TheScriptFailsLoudlyAndVerifiesItsWork()
    {
        var script = InstallScriptWriter.ReadScript(InstallScriptWriter.RdpListenerScriptName);

        Assert.Contains("exit 1", script, StringComparison.Ordinal);
        Assert.Contains("exit 0", script, StringComparison.Ordinal);
        Assert.Contains("SSLCertificateSHA1Hash", script, StringComparison.Ordinal);

        // Reads the value back rather than trusting the write.
        Assert.Contains("$applied", script, StringComparison.Ordinal);

        // wmic is deprecated and being removed from Windows. The header comment names
        // it when explaining why it is avoided, so only the executable body counts.
        Assert.DoesNotContain("wmic", BodyOf(script), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Set-CimInstance", script, StringComparison.Ordinal);
    }

    /// <summary>The script with its leading comment-based help removed.</summary>
    private static string BodyOf(string script)
    {
        var end = script.IndexOf("#>", StringComparison.Ordinal);

        Assert.True(end >= 0, "Expected the script to open with a comment-based help block.");
        return script[(end + 2)..];
    }

    [Fact]
    public void AnUnknownScriptNameIsAnError()
    {
        Assert.Throws<InvalidOperationException>(() => InstallScriptWriter.ReadScript("Nope.ps1"));
    }

    // --------------------------------------------------------------- writing it

    [Fact]
    public void WritingPutsTheScriptWhereWinAcmeCanRunIt()
    {
        var path = InstallScriptWriter.WriteScript(_workspace, InstallScriptWriter.RdpListenerScriptName);

        Assert.True(File.Exists(path));
        Assert.Equal(
            InstallScriptWriter.ReadScript(InstallScriptWriter.RdpListenerScriptName),
            File.ReadAllText(path));
    }

    [Fact]
    public void WritingCreatesAMissingFolder()
    {
        var nested = Path.Combine(_workspace, "Scripts");

        InstallScriptWriter.WriteScript(nested, InstallScriptWriter.RdpListenerScriptName);

        Assert.True(File.Exists(Path.Combine(nested, InstallScriptWriter.RdpListenerScriptName)));
    }

    [Fact]
    public void AnIdenticalFileIsLeftAlone()
    {
        var path = InstallScriptWriter.WriteScript(_workspace, InstallScriptWriter.RdpListenerScriptName);
        var written = File.GetLastWriteTimeUtc(path);

        Assert.True(InstallScriptWriter.IsUpToDate(_workspace, InstallScriptWriter.RdpListenerScriptName));

        InstallScriptWriter.WriteScript(_workspace, InstallScriptWriter.RdpListenerScriptName);

        Assert.Equal(written, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void AChangedFileIsReplaced()
    {
        var path = InstallScriptWriter.WriteScript(_workspace, InstallScriptWriter.RdpListenerScriptName);
        File.WriteAllText(path, "# someone edited this");

        Assert.False(InstallScriptWriter.IsUpToDate(_workspace, InstallScriptWriter.RdpListenerScriptName));

        InstallScriptWriter.WriteScript(_workspace, InstallScriptWriter.RdpListenerScriptName);

        Assert.Equal(
            InstallScriptWriter.ReadScript(InstallScriptWriter.RdpListenerScriptName),
            File.ReadAllText(path));
    }

    [Fact]
    public void AnAbsentFileIsNotUpToDate()
    {
        Assert.False(InstallScriptWriter.IsUpToDate(_workspace, InstallScriptWriter.RdpListenerScriptName));
        Assert.False(InstallScriptWriter.IsUpToDate("  ", InstallScriptWriter.RdpListenerScriptName));
    }

    // ------------------------------------------------------------- the preset

    [Fact]
    public void OurPresetPassesNamedParameters()
    {
        var args = ArgumentsFor(InstallationPresets.RdListenerToolId);

        Assert.Equal("script", ValueAfter(args, "--installation"));
        Assert.EndsWith(
            InstallScriptWriter.RdpListenerScriptName,
            ValueAfter(args, "--script"),
            StringComparison.Ordinal);

        Assert.Equal(
            "-Thumbprint '{CertThumbprint}' -CacheFile '{CacheFile}' -CachePassword '{CachePassword}'",
            ValueAfter(args, "--scriptparameters"));
    }

    [Fact]
    public void OurPresetIsMarkedAsShippedByThisTool()
    {
        var ours = InstallationPresets.Find(InstallationPresets.RdListenerToolId);
        var bundled = InstallationPresets.Find(InstallationPresets.RdListenerBundledId);

        Assert.NotNull(ours);
        Assert.NotNull(bundled);
        Assert.True(ours.ProvidedByTool);
        Assert.False(bundled.ProvidedByTool);
    }

    /// <summary>
    /// Ours handles the store itself, so unlike the bundled script it does not force
    /// the certificate store to My.
    /// </summary>
    [Fact]
    public void OurPresetDoesNotConstrainTheCertificateStore()
    {
        Assert.Empty(InstallationPresets.Find(InstallationPresets.RdListenerToolId)!.RequiredStoreName);
        Assert.Equal("My", InstallationPresets.Find(InstallationPresets.RdListenerBundledId)!.RequiredStoreName);

        var definition = TestData.ValidCloudflareDefinition();
        definition.Installation = new InstallationSettings
        {
            ScriptPresetId = InstallationPresets.RdListenerToolId,
        };

        Assert.Empty(RenewalValidator.Validate(definition));
    }

    /// <summary>The bundled preset must keep behaving exactly as it did.</summary>
    [Fact]
    public void TheBundledPresetIsUnchanged()
    {
        var args = ArgumentsFor(InstallationPresets.RdListenerBundledId);

        Assert.EndsWith("ImportRDListener.ps1", ValueAfter(args, "--script"), StringComparison.Ordinal);
        Assert.Equal("{CertThumbprint}", ValueAfter(args, "--scriptparameters"));
    }

    [Fact]
    public void BothRdpPresetsAreOffered()
    {
        var ids = InstallationPresets.Scripts.Select(p => p.Id).ToList();

        Assert.Contains(InstallationPresets.RdListenerBundledId, ids);
        Assert.Contains(InstallationPresets.RdListenerToolId, ids);
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
