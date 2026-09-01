namespace WinAcmeGui.Core.Plugins;

/// <summary>
/// A win-acme validation plugin as far as this GUI is concerned: an id to pass to
/// <c>--validation</c> and the parameters it needs.
/// </summary>
public sealed class ValidationPlugin
{
    /// <summary>Value passed to <c>--validation</c>.</summary>
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public string Description { get; init; } = string.Empty;

    /// <summary>Value passed to <c>--validationmode</c>.</summary>
    public string ValidationMode { get; init; } = "dns-01";

    public required IReadOnlyList<PluginField> Fields { get; init; }

    /// <summary>
    /// Checks that cannot be expressed as "this field is required" — for example
    /// Route53 accepting either an IAM role or an access key pair. Returns one
    /// message per problem, or nothing when the values are usable.
    /// </summary>
    public Func<ValuesLookup, IEnumerable<string>>? ExtraValidation { get; init; }

    public PluginField? FindField(string name) =>
        Fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Reads plugin field values, treating missing and blank as the same thing so the
/// validation rules do not have to keep checking both.
/// </summary>
public sealed class ValuesLookup(IReadOnlyDictionary<string, string> values)
{
    public string this[string name] =>
        values.TryGetValue(name, out var value) ? value.Trim() : string.Empty;

    public bool Has(string name) => !string.IsNullOrWhiteSpace(this[name]);

    public bool IsTrue(string name) =>
        bool.TryParse(this[name], out var parsed) && parsed;
}
