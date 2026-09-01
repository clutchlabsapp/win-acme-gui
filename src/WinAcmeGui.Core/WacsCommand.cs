using System.Text;

namespace WinAcmeGui.Core;

/// <summary>One command line argument, flagged when it holds a credential.</summary>
public sealed record WacsArgument(string Value, bool IsSecret = false);

/// <summary>
/// A wacs.exe invocation. Arguments are kept as a list rather than a single string
/// so they can be handed to <c>ProcessStartInfo.ArgumentList</c>, which quotes them
/// correctly; the joined form exists only for display.
/// </summary>
public sealed class WacsCommand(string executablePath, IReadOnlyList<WacsArgument> arguments)
{
    public const string RedactedPlaceholder = "********";

    public string ExecutablePath { get; } = executablePath;

    public IReadOnlyList<WacsArgument> Arguments { get; } = arguments;

    /// <summary>The raw argument values, in order, for <c>ProcessStartInfo.ArgumentList</c>.</summary>
    public IReadOnlyList<string> ArgumentValues { get; } = arguments.Select(a => a.Value).ToArray();

    public bool ContainsSecrets => Arguments.Any(a => a.IsSecret);

    /// <summary>
    /// Renders the command for the preview pane. With <paramref name="redactSecrets"/>
    /// set — the default — every credential is replaced, so the preview is safe to
    /// paste into a support thread.
    /// </summary>
    public string ToDisplayString(bool redactSecrets = true)
    {
        // The executable is always quoted: real installs live under
        // "C:\Program Files\win-acme", and a copied command line has to survive that.
        var builder = new StringBuilder(QuoteAlways(ExecutablePath));

        foreach (var argument in Arguments)
        {
            var value = argument.IsSecret && redactSecrets ? RedactedPlaceholder : argument.Value;
            builder.Append(' ').Append(Quote(value));
        }

        return builder.ToString();
    }

    public override string ToString() => ToDisplayString();

    private static string Quote(string value) =>
        value.Length > 0 && !value.Any(char.IsWhiteSpace) && !value.Contains('"')
            ? value
            : QuoteAlways(value);

    private static string QuoteAlways(string value) =>
        "\"" + value.Replace("\"", "\\\"") + "\"";
}
