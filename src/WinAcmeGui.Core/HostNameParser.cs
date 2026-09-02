using System.Text.RegularExpressions;

namespace WinAcmeGui.Core;

/// <summary>
/// Turns whatever the user typed into the host names box into a clean list, and
/// says whether each one could plausibly be a public DNS name.
/// </summary>
public static partial class HostNameParser
{
    private static readonly char[] Separators = [',', ';', ' ', '\t', '\n', '\r'];

    private const int MaxHostNameLength = 253;
    private const int MaxLabelLength = 63;

    /// <summary>
    /// Splits on commas, semicolons and any whitespace, trims, lowercases and removes
    /// duplicates while keeping the order the user typed them in.
    /// </summary>
    public static List<string> Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var part in raw.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var host = Normalize(part);
            if (host.Length > 0 && seen.Add(host))
            {
                result.Add(host);
            }
        }

        return result;
    }

    /// <summary>Trims, drops a trailing dot and lowercases.</summary>
    public static string Normalize(string? host) =>
        (host ?? string.Empty).Trim().TrimEnd('.').ToLowerInvariant();

    /// <summary>
    /// True for names a public CA could issue for: at least two labels, valid
    /// characters, and optionally a leading wildcard.
    /// </summary>
    public static bool IsValid(string? host)
    {
        var candidate = Normalize(host);

        if (candidate.Length is 0 or > MaxHostNameLength)
        {
            return false;
        }

        if (candidate.StartsWith("*.", StringComparison.Ordinal))
        {
            candidate = candidate[2..];
        }

        var labels = candidate.Split('.');
        if (labels.Length < 2)
        {
            return false;
        }

        if (labels.Any(label => label.Length is 0 or > MaxLabelLength))
        {
            return false;
        }

        return LabelPattern().IsMatch(candidate) && TopLevelLabelPattern().IsMatch(labels[^1]);
    }

    /// <summary>True when the name is a wildcard, which forces DNS validation.</summary>
    public static bool IsWildcard(string? host) =>
        Normalize(host).StartsWith("*.", StringComparison.Ordinal);

    [GeneratedRegex(@"^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$")]
    private static partial Regex LabelPattern();

    /// <summary>
    /// The last label must start with a letter, which rules out an IP address, but
    /// still has to accept punycode TLDs such as <c>xn--p1ai</c>.
    /// </summary>
    [GeneratedRegex(@"^[a-z][a-z0-9-]*[a-z0-9]$")]
    private static partial Regex TopLevelLabelPattern();
}
