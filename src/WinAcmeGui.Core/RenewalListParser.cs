using System.Text.RegularExpressions;
using WinAcmeGui.Core.Models;

namespace WinAcmeGui.Core;

/// <summary>
/// Reads the output of <c>wacs.exe --list</c>.
/// </summary>
/// <remarks>
/// win-acme prints one numbered line per renewal, built by <c>Renewal.ToString</c>:
/// <code>
///  1: example.com - 3 renewals, due 2026/10/1
///  2: rdp.example.com - 1 renewal, due now, 2 errors
/// </code>
/// and <c>[empty]</c> when nothing is configured. There is no machine-readable
/// option, so this parser is deliberately forgiving: anything it does not recognise
/// is skipped rather than treated as an error, and the raw line is always kept.
/// </remarks>
public static partial class RenewalListParser
{
    public static IReadOnlyList<RenewalSummary> Parse(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        var results = new List<RenewalSummary>();

        foreach (var line in output.Split('\n'))
        {
            var summary = ParseLine(line);
            if (summary is not null)
            {
                results.Add(summary);
            }
        }

        return results;
    }

    /// <summary>
    /// True when win-acme explicitly reported that there are no renewals, as opposed
    /// to producing output this parser could not read.
    /// </summary>
    public static bool IsEmptyList(string? output) =>
        output is not null && output.Contains("[empty]", StringComparison.Ordinal);

    public static RenewalSummary? ParseLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var trimmed = line.TrimEnd('\r', '\n');

        var numbered = NumberedLinePattern().Match(trimmed);
        if (!numbered.Success)
        {
            return null;
        }

        var description = numbered.Groups["description"].Value;
        var renewal = RenewalDescriptionPattern().Match(description);
        if (!renewal.Success)
        {
            return null;
        }

        var rest = renewal.Groups["rest"].Value;
        var isDue = DueNowPattern().IsMatch(rest);

        return new RenewalSummary
        {
            Index = int.Parse(numbered.Groups["index"].Value),
            FriendlyName = renewal.Groups["name"].Value.Trim(),
            SuccessfulRenewals = int.Parse(renewal.Groups["success"].Value),
            Orders = ReadCount(OrdersPattern().Match(rest), "orders", fallback: 1),
            IsDue = isDue,
            DueDescription = isDue ? string.Empty : ReadDueDate(rest),
            Errors = ReadCount(ErrorsPattern().Match(rest), "errors", fallback: 0),
            RawLine = trimmed.Trim(),
        };
    }

    private static string ReadDueDate(string rest)
    {
        var match = DueDatePattern().Match(rest);
        return match.Success ? match.Groups["due"].Value.Trim() : string.Empty;
    }

    private static int ReadCount(Match match, string group, int fallback) =>
        match.Success && int.TryParse(match.Groups[group].Value, out var value) ? value : fallback;

    /// <summary>Requires a numeric index, which keeps banner lines such as "Web: ..." out.</summary>
    [GeneratedRegex(@"^\s*(?<index>\d+):\s+(?<description>.+?)\s*$")]
    private static partial Regex NumberedLinePattern();

    [GeneratedRegex(@"^(?<name>.+?) - (?<success>\d+) renewals?(?<rest>.*)$")]
    private static partial Regex RenewalDescriptionPattern();

    [GeneratedRegex(@",\s*(?<orders>\d+) orders?\b")]
    private static partial Regex OrdersPattern();

    [GeneratedRegex(@",\s*due now\b")]
    private static partial Regex DueNowPattern();

    [GeneratedRegex(@",\s*due (?<due>[^,]+)")]
    private static partial Regex DueDatePattern();

    [GeneratedRegex(@",\s*(?<errors>\d+) errors?\b")]
    private static partial Regex ErrorsPattern();
}
