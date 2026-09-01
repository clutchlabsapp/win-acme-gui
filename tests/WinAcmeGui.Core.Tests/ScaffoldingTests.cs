using WinAcmeGui.Core.Models;
using Xunit;

namespace WinAcmeGui.Core.Tests;

public class ScaffoldingTests
{
    [Fact]
    public void CoreLibraryIsReferencedAndLoadable()
    {
        Assert.Equal("win-acme GUI", BuildInfo.ProductName);
    }
}
