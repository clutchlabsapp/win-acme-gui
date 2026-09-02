using System.IO.Compression;
using WinAcmeGui.Core;
using Xunit;

namespace WinAcmeGui.Core.Tests;

public class WinAcmeInstallerTests : IDisposable
{
    private readonly string _workspace = Path.Combine(
        Path.GetTempPath(), "win-acme-gui-tests-" + Guid.NewGuid().ToString("N"));

    public WinAcmeInstallerTests() => Directory.CreateDirectory(_workspace);

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

    private string MakeZip(string name, params (string EntryName, string Content)[] entries)
    {
        var path = Path.Combine(_workspace, name);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);

        foreach (var (entryName, content) in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(entryName).Open());
            writer.Write(content);
        }

        return path;
    }

    [Fact]
    public void UnpacksEveryEntryIntoTheTargetFolder()
    {
        var zip = MakeZip("good.zip",
            ("wacs.exe", "not really an executable"),
            ("settings_default.json", "{}"),
            ("Scripts/ImportRDListener.ps1", "param($t)"));

        var target = Path.Combine(_workspace, "install");
        WinAcmeInstaller.ExtractSafely(zip, target);

        Assert.True(File.Exists(Path.Combine(target, "wacs.exe")));
        Assert.True(File.Exists(Path.Combine(target, "settings_default.json")));
        Assert.True(File.Exists(Path.Combine(target, "Scripts", "ImportRDListener.ps1")));
    }

    [Fact]
    public void OverwritesExistingFilesSoAnUpgradeWorks()
    {
        var target = Path.Combine(_workspace, "install");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "wacs.exe"), "old version");

        WinAcmeInstaller.ExtractSafely(MakeZip("upgrade.zip", ("wacs.exe", "new version")), target);

        Assert.Equal("new version", File.ReadAllText(Path.Combine(target, "wacs.exe")));
    }

    [Fact]
    public void PluginZipsUnpackAlongsideAnExistingInstall()
    {
        var target = Path.Combine(_workspace, "install");

        WinAcmeInstaller.ExtractSafely(MakeZip("main.zip", ("wacs.exe", "main")), target);
        WinAcmeInstaller.ExtractSafely(
            MakeZip("plugin.zip", ("plugin.validation.dns.cloudflare.dll", "plugin")), target);

        Assert.True(File.Exists(Path.Combine(target, "wacs.exe")));
        Assert.True(File.Exists(Path.Combine(target, "plugin.validation.dns.cloudflare.dll")));
    }

    [Fact]
    public void RefusesAnArchiveThatWouldEscapeTheTargetFolder()
    {
        var zip = MakeZip("evil.zip", ("../escaped.txt", "pwned"));
        var target = Path.Combine(_workspace, "install");

        var error = Assert.Throws<InvalidOperationException>(
            () => WinAcmeInstaller.ExtractSafely(zip, target));

        Assert.Contains("outside the target folder", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_workspace, "escaped.txt")));
    }

    [Fact]
    public void TheDefaultInstallFolderIsWhereWinAcmeRecommends()
    {
        Assert.EndsWith("win-acme", WinAcmeInstaller.DefaultInstallFolder, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheArchitectureIsOneWinAcmePublishesBuildsFor()
    {
        Assert.Contains(WinAcmeInstaller.CurrentArchitecture, new[] { "x64", "x86", "arm64" });
    }
}
