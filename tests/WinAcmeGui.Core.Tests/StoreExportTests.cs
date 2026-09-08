using WinAcmeGui.Core;
using WinAcmeGui.Core.Models;
using Xunit;

namespace WinAcmeGui.Core.Tests;

public class StoreExportTests
{
    private const string Folder = @"C:\certs\export";

    private static RenewalDefinition WithStore(bool pfx, bool pem, string folder = Folder)
    {
        var definition = TestData.ValidCloudflareDefinition();

        definition.Store.ExportPfx = pfx;
        definition.Store.ExportPem = pem;
        definition.Store.ExportFolder = folder;

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
    public void NeitherTickedLeavesTheCommandExactlyAsItWas()
    {
        var args = ArgumentsFor(WithStore(pfx: false, pem: false, folder: string.Empty));

        Assert.Equal("certificatestore", ValueAfter(args, "--store"));
        Assert.DoesNotContain("--pfxfilepath", args);
        Assert.DoesNotContain("--pemfilespath", args);
    }

    [Fact]
    public void PfxIsAddedAlongsideTheWindowsStore()
    {
        var args = ArgumentsFor(WithStore(pfx: true, pem: false));

        Assert.Equal("certificatestore,pfxfile", ValueAfter(args, "--store"));
        Assert.Equal(Folder, ValueAfter(args, "--pfxfilepath"));
        Assert.DoesNotContain("--pemfilespath", args);
    }

    [Fact]
    public void PemIsAddedAlongsideTheWindowsStore()
    {
        var args = ArgumentsFor(WithStore(pfx: false, pem: true));

        Assert.Equal("certificatestore,pemfiles", ValueAfter(args, "--store"));
        Assert.Equal(Folder, ValueAfter(args, "--pemfilespath"));
        Assert.DoesNotContain("--pfxfilepath", args);
    }

    [Fact]
    public void BothFormatsShareOneFolder()
    {
        var args = ArgumentsFor(WithStore(pfx: true, pem: true));

        Assert.Equal("certificatestore,pfxfile,pemfiles", ValueAfter(args, "--store"));
        Assert.Equal(Folder, ValueAfter(args, "--pfxfilepath"));
        Assert.Equal(Folder, ValueAfter(args, "--pemfilespath"));
    }

    /// <summary>
    /// The Windows store is never dropped: IIS bindings and the RD and Exchange scripts
    /// all read the certificate back out of it.
    /// </summary>
    [Fact]
    public void TheWindowsStoreSurvivesEveryCombination()
    {
        foreach (var (pfx, pem) in new[] { (true, false), (false, true), (true, true) })
        {
            var store = ValueAfter(ArgumentsFor(WithStore(pfx, pem)), "--store");
            Assert.StartsWith("certificatestore", store, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheStoreNameAndKeepExistingStillApply()
    {
        var definition = WithStore(pfx: true, pem: false);
        definition.Store.StoreName = "My";
        definition.Store.KeepExisting = true;

        var args = ArgumentsFor(definition);

        Assert.Equal("My", ValueAfter(args, "--certificatestore"));
        Assert.Contains("--keepexisting", args);
    }

    [Fact]
    public void ABlankFolderEmitsNoDanglingPathFlag()
    {
        var args = ArgumentsFor(WithStore(pfx: true, pem: true, folder: "   "));

        Assert.DoesNotContain("--pfxfilepath", args);
        Assert.DoesNotContain("--pemfilespath", args);
    }

    [Fact]
    public void ExportingWithNowhereToWriteIsRejected()
    {
        Assert.Contains(
            RenewalValidator.Validate(WithStore(pfx: true, pem: false, folder: "  ")),
            p => p.Contains("export", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(
            RenewalValidator.Validate(WithStore(pfx: false, pem: true, folder: string.Empty)),
            p => p.Contains("export", StringComparison.OrdinalIgnoreCase));

        Assert.Empty(RenewalValidator.Validate(WithStore(pfx: true, pem: true)));
        Assert.Empty(RenewalValidator.Validate(WithStore(pfx: false, pem: false, folder: string.Empty)));
    }

    [Fact]
    public void ExportsFilesReflectsTheCheckboxes()
    {
        Assert.False(new StoreSettings().ExportsFiles);
        Assert.True(new StoreSettings { ExportPfx = true }.ExportsFiles);
        Assert.True(new StoreSettings { ExportPem = true }.ExportsFiles);
    }

    [Fact]
    public void ADryRunIgnoresTheExportSettingsEntirely()
    {
        var args = WacsArgumentBuilder
            .BuildStagingTest(TestData.WacsPath, WithStore(pfx: true, pem: true))
            .ArgumentValues;

        Assert.Equal("none", ValueAfter(args, "--store"));
        Assert.DoesNotContain("--pfxfilepath", args);
        Assert.DoesNotContain("--pemfilespath", args);
    }
}
