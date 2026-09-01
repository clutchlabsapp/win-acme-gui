using WinAcmeGui.Core;
using Xunit;

namespace WinAcmeGui.Core.Tests;

public class WacsLocatorTests
{
    [Fact]
    public void PrefersTheRememberedPathWhenItStillExists()
    {
        var remembered = @"D:\tools\win-acme\wacs.exe";

        var found = WacsLocator.Locate(remembered, path => path == remembered);

        Assert.Equal(remembered, found);
    }

    [Fact]
    public void FallsBackToTheConventionalLocationsWhenTheRememberedPathIsGone()
    {
        var installed = WacsLocator.CandidatePaths()[0];

        var found = WacsLocator.Locate(@"D:\gone\wacs.exe", path => path == installed);

        Assert.Equal(installed, found);
    }

    [Fact]
    public void ReturnsNullWhenWinAcmeIsNotInstalled()
    {
        Assert.Null(WacsLocator.Locate(null, _ => false));
        Assert.Null(WacsLocator.Locate("   ", _ => false));
    }

    [Fact]
    public void CandidatesAreDistinctAndAllPointAtWacsExe()
    {
        var candidates = WacsLocator.CandidatePaths();

        Assert.NotEmpty(candidates);
        Assert.All(candidates, path => Assert.EndsWith("wacs.exe", path, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            candidates.Count,
            candidates.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void ScriptsFolderSitsNextToTheExecutable()
    {
        var scripts = WacsLocator.ScriptsFolder(Path.Combine("C:", "win-acme", "wacs.exe"));

        Assert.EndsWith("Scripts", scripts, StringComparison.Ordinal);
        Assert.Contains("win-acme", scripts, StringComparison.Ordinal);
    }

    [Fact]
    public void ScriptsFolderOfABareFileNameIsEmptyRatherThanWrong()
    {
        Assert.Equal(string.Empty, WacsLocator.ScriptsFolder("wacs.exe"));
    }
}
