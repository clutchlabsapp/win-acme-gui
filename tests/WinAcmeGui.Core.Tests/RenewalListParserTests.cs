using WinAcmeGui.Core;
using Xunit;

namespace WinAcmeGui.Core.Tests;

public class RenewalListParserTests
{
    /// <summary>
    /// Shaped like real output: a banner, then the numbered renewal lines that
    /// Renewal.ToString produces.
    /// </summary>
    private const string SampleOutput = """
         A simple Windows ACMEv2 client (WACS)
         Software version 2.2.9.1701 (release, trimmed, standalone, 64-bit)
         Connecting to https://acme-v02.api.letsencrypt.org/
         Server URI: https://acme-v02.api.letsencrypt.org/directory
         Web: https://www.win-acme.com/

         1: example.com - 3 renewals, due 2026/10/1
         2: rdp.example.com - 1 renewal, due now, 2 errors
         3: shop.example.com - 0 renewals, 2 orders, due 2026/9/15 ~ 2026/9/22
        """;

    [Fact]
    public void ReadsEveryRenewalLineAndIgnoresTheBanner()
    {
        var renewals = RenewalListParser.Parse(SampleOutput);

        Assert.Equal(3, renewals.Count);
        Assert.Equal(new[] { "example.com", "rdp.example.com", "shop.example.com" },
            renewals.Select(r => r.FriendlyName));
    }

    [Fact]
    public void ReadsTheRenewalCountAndDueDate()
    {
        var renewal = RenewalListParser.Parse(SampleOutput)[0];

        Assert.Equal(1, renewal.Index);
        Assert.Equal("example.com", renewal.FriendlyName);
        Assert.Equal(3, renewal.SuccessfulRenewals);
        Assert.False(renewal.IsDue);
        Assert.Equal("2026/10/1", renewal.DueDescription);
        Assert.Equal(0, renewal.Errors);
        Assert.False(renewal.HasErrors);
    }

    [Fact]
    public void ReadsDueNowAndErrorCount()
    {
        var renewal = RenewalListParser.Parse(SampleOutput)[1];

        Assert.True(renewal.IsDue);
        Assert.Equal(string.Empty, renewal.DueDescription);
        Assert.Equal(2, renewal.Errors);
        Assert.True(renewal.HasErrors);
        Assert.Equal(1, renewal.SuccessfulRenewals);
    }

    [Fact]
    public void ReadsAnOrderCountAndADateRange()
    {
        var renewal = RenewalListParser.Parse(SampleOutput)[2];

        Assert.Equal(2, renewal.Orders);
        Assert.Equal("2026/9/15 ~ 2026/9/22", renewal.DueDescription);
        Assert.Equal(0, renewal.SuccessfulRenewals);
    }

    [Fact]
    public void DefaultsToASingleOrderWhenWinAcmeDoesNotMentionOrders()
    {
        Assert.Equal(1, RenewalListParser.Parse(SampleOutput)[0].Orders);
    }

    [Fact]
    public void KeepsTheRawLineForDisplay()
    {
        Assert.Equal(
            "1: example.com - 3 renewals, due 2026/10/1",
            RenewalListParser.Parse(SampleOutput)[0].RawLine);
    }

    [Fact]
    public void AMachineWithNoRenewalsGivesAnEmptyListNotAnError()
    {
        const string output = """
             A simple Windows ACMEv2 client (WACS)
             [empty]
            """;

        Assert.Empty(RenewalListParser.Parse(output));
        Assert.True(RenewalListParser.IsEmptyList(output));
    }

    [Fact]
    public void UnreadableOutputIsSkippedRatherThanThrowing()
    {
        var renewals = RenewalListParser.Parse("garbage\n\n!!!\nWeb: https://www.win-acme.com/\n42\n");

        Assert.Empty(renewals);
        Assert.False(RenewalListParser.IsEmptyList("garbage"));
    }

    [Fact]
    public void NullAndBlankInputAreHandled()
    {
        Assert.Empty(RenewalListParser.Parse(null));
        Assert.Empty(RenewalListParser.Parse("   "));
        Assert.Null(RenewalListParser.ParseLine(null));
        Assert.Null(RenewalListParser.ParseLine("   "));
    }

    [Fact]
    public void HandlesWindowsLineEndings()
    {
        var renewals = RenewalListParser.Parse(" 1: example.com - 3 renewals, due now\r\n 2: b.example.com - 1 renewal, due now\r\n");

        Assert.Equal(2, renewals.Count);
        Assert.Equal("b.example.com", renewals[1].FriendlyName);
    }

    [Fact]
    public void AFriendlyNameContainingASpaceOrDashIsReadWhole()
    {
        var renewal = RenewalListParser.ParseLine(" 7: My web - server cert - 5 renewals, due now");

        Assert.NotNull(renewal);
        Assert.Equal("My web - server cert", renewal.FriendlyName);
        Assert.Equal(5, renewal.SuccessfulRenewals);
        Assert.Equal(7, renewal.Index);
    }
}
