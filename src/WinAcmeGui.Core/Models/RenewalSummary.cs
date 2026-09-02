namespace WinAcmeGui.Core.Models;

/// <summary>
/// One line of <c>wacs.exe --list</c>. win-acme prints a human sentence rather than
/// a machine format, so the due date stays a string: its format follows the user's
/// <c>settings.json</c> and is not worth guessing at.
/// </summary>
public sealed record RenewalSummary
{
    /// <summary>The number win-acme printed at the start of the line.</summary>
    public required int Index { get; init; }

    /// <summary>
    /// The renewal's friendly name. <c>--list</c> does not print renewal ids, so this
    /// is what the GUI passes to <c>--friendlyname</c> when renewing a single one.
    /// </summary>
    public required string FriendlyName { get; init; }

    /// <summary>How many times this renewal has succeeded.</summary>
    public int SuccessfulRenewals { get; init; }

    /// <summary>Number of orders, when win-acme reported more than one.</summary>
    public int Orders { get; init; } = 1;

    /// <summary>True when win-acme said "due now".</summary>
    public bool IsDue { get; init; }

    /// <summary>The due date exactly as win-acme printed it, or blank when it said "due now".</summary>
    public string DueDescription { get; init; } = string.Empty;

    /// <summary>Consecutive failures at the end of the history. Anything above zero needs attention.</summary>
    public int Errors { get; init; }

    /// <summary>The original line, kept so the UI can always fall back to showing it verbatim.</summary>
    public required string RawLine { get; init; }

    public bool HasErrors => Errors > 0;
}
