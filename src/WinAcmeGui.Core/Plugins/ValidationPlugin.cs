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

    /// <summary>
    /// Set when this provider is driven through win-acme's built-in <c>script</c>
    /// plugin and an external helper program, rather than by a win-acme plugin of its
    /// own. See <see cref="Plugins.ScriptWiring"/>.
    /// </summary>
    public ScriptWiring? ScriptWiring { get; init; }

    /// <summary>
    /// False when win-acme can already do this without downloading anything. Only the
    /// provider-specific DNS plugins ship as separate downloads; the <c>script</c>
    /// plugin is built in, and so works on the trimmed build too.
    /// </summary>
    public bool RequiresSeparateDownload { get; init; } = true;

    /// <summary>True when the provider needs an external helper program.</summary>
    public bool UsesScript => ScriptWiring is not null;

    public required IReadOnlyList<PluginField> Fields { get; init; }

    /// <summary>
    /// Checks that cannot be expressed as "this field is required" — for example
    /// Route53 accepting either an IAM role or an access key pair. Returns one
    /// message per problem, or nothing when the values are usable.
    /// </summary>
    public Func<ValuesLookup, IEnumerable<string>>? ExtraValidation { get; init; }

    /// <summary>
    /// One argument that only this plugin contributes, used to tell from wacs.exe
    /// --help output whether the plugin is installed. It has to be distinctive:
    /// the Azure DNS plugin and the KeyVault store plugin share several arguments,
    /// so matching on any of them would give a false positive.
    /// </summary>
    public string DetectionFlag { get; init; } = string.Empty;

    /// <summary>The detection flag, falling back to the first field.</summary>
    public string EffectiveDetectionFlag =>
        DetectionFlag.Length > 0 ? "--" + DetectionFlag : Fields[0].Flag;

    public PluginField? FindField(string name) =>
        Fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// How to drive an external helper program through win-acme's <c>script</c> validation
/// plugin: which field holds the path to it, and what to pass on create and delete.
/// </summary>
/// <remarks>
/// win-acme substitutes <c>{Identifier}</c>, <c>{RecordName}</c>, <c>{ZoneName}</c>,
/// <c>{NodeName}</c> and <c>{Token}</c> into these argument strings.
/// </remarks>
public sealed class ScriptWiring
{
    /// <summary>Name of the field holding the helper's path; becomes <c>--dnsscript</c>.</summary>
    public required string ExecutableFieldName { get; init; }

    /// <summary>Value for <c>--dnscreatescriptarguments</c>.</summary>
    public required string CreateArguments { get; init; }

    /// <summary>Value for <c>--dnsdeletescriptarguments</c>.</summary>
    public required string DeleteArguments { get; init; }

    /// <summary>
    /// The file name the helper is actually built as, used to spot when the user has
    /// browsed to the wrong thing.
    /// </summary>
    public string ExpectedFileName { get; init; } = string.Empty;
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
