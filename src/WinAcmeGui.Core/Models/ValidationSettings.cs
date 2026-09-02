namespace WinAcmeGui.Core.Models;

/// <summary>
/// Which validation plugin to use and the values for its parameters. The keys are
/// plugin field names (see <c>PluginField.Name</c>), not command line flags.
/// </summary>
public sealed class ValidationSettings
{
    /// <summary>Emits <c>--validation</c>. Must match a plugin id in the catalog.</summary>
    public string PluginId { get; set; } = string.Empty;

    public Dictionary<string, string> Values { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public string this[string fieldName]
    {
        get => Values.TryGetValue(fieldName, out var value) ? value : string.Empty;
        set => Values[fieldName] = value;
    }
}
