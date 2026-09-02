namespace WinAcmeGui.Core.Plugins;

public enum PluginFieldKind
{
    /// <summary>Free text, shown in the clear.</summary>
    Text,

    /// <summary>An API token or password. Masked in the UI and redacted in the command preview.</summary>
    Secret,

    /// <summary>A switch. Emits the bare flag when true and nothing when false.</summary>
    Boolean,

    /// <summary>A path to a file on disk, shown with a Browse button.</summary>
    FilePath,
}

/// <summary>Where a field's value ends up.</summary>
public enum PluginFieldDestination
{
    /// <summary>Passed to wacs.exe as a command line argument.</summary>
    CommandLine,

    /// <summary>
    /// Written to a configuration file belonging to an external helper program, and
    /// never placed on a command line. Used by helpers that read their own credentials
    /// from disk, which keeps API keys out of the process arguments entirely.
    /// </summary>
    ExternalFile,
}

/// <summary>
/// One parameter of a win-acme plugin. The UI renders these generically, so adding
/// support for another plugin means adding entries here and nothing else.
/// </summary>
public sealed class PluginField
{
    /// <summary>
    /// The command line flag without its leading dashes, e.g. <c>cloudflareapitoken</c>.
    /// For an <see cref="PluginFieldDestination.ExternalFile"/> field this is just an
    /// identifier, since no flag is ever emitted.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>Label shown next to the input.</summary>
    public required string Label { get; init; }

    public PluginFieldKind Kind { get; init; } = PluginFieldKind.Text;

    public PluginFieldDestination Destination { get; init; } = PluginFieldDestination.CommandLine;

    /// <summary>When true the field must be filled in before the command can be built.</summary>
    public bool Required { get; init; } = true;

    /// <summary>One-line hint shown under the input.</summary>
    public string Help { get; init; } = string.Empty;

    public string Flag => "--" + Name;

    public bool IsSecret => Kind == PluginFieldKind.Secret;

    /// <summary>True when this value must never appear in a wacs.exe argument list.</summary>
    public bool IsExternal => Destination == PluginFieldDestination.ExternalFile;
}
