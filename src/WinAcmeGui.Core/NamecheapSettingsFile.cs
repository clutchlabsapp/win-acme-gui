using System.Text.Json;
using System.Text.Json.Nodes;

namespace WinAcmeGui.Core;

/// <summary>The four values Fynydd.NameCheap needs to talk to the Namecheap API.</summary>
public sealed record NamecheapCredentials(
    string ApiKey,
    string UserName,
    string ApiUserName,
    string ClientIp)
{
    public static NamecheapCredentials Empty { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty);

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(UserName)
        && !string.IsNullOrWhiteSpace(ApiUserName)
        && !string.IsNullOrWhiteSpace(ClientIp);

    /// <summary>Names of the values that are still blank, for a useful error message.</summary>
    public IReadOnlyList<string> MissingValues
    {
        get
        {
            var missing = new List<string>();

            if (string.IsNullOrWhiteSpace(ApiKey))
            {
                missing.Add("API key");
            }

            if (string.IsNullOrWhiteSpace(UserName))
            {
                missing.Add("username");
            }

            if (string.IsNullOrWhiteSpace(ApiUserName))
            {
                missing.Add("API username");
            }

            if (string.IsNullOrWhiteSpace(ClientIp))
            {
                missing.Add("whitelisted IP");
            }

            return missing;
        }
    }
}

/// <summary>
/// Reads and writes the <c>appsettings.json</c> that Fynydd.NameCheap keeps its
/// credentials in.
/// </summary>
/// <remarks>
/// The helper loads this file from the <em>current working directory</em>, not from
/// beside itself, and win-acme does not set a working directory when it runs a script.
/// In practice that means the helper has to sit in the folder win-acme runs from —
/// see <see cref="ExpectedPath"/> and the location check in the UI.
/// <para>
/// Upstream writes the key as <c>ClientIP</c> but reads it as <c>ClientIp</c>. That
/// works because .NET configuration keys are case-insensitive; this class reads either
/// casing and writes the one upstream ships.
/// </para>
/// </remarks>
public static class NamecheapSettingsFile
{
    public const string FileName = "appsettings.json";

    private const string SectionName = "NameCheap";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>The settings file that belongs to a given helper executable.</summary>
    public static string ExpectedPath(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return string.Empty;
        }

        var directory = Path.GetDirectoryName(executablePath.Trim());
        return string.IsNullOrEmpty(directory) ? FileName : Path.Combine(directory, FileName);
    }

    /// <summary>
    /// Reads the credentials, returning <see cref="NamecheapCredentials.Empty"/> when
    /// the file is missing, unreadable or not valid JSON. A broken file is a thing to
    /// report in the UI, not an exception to throw at the user.
    /// </summary>
    public static NamecheapCredentials Read(string? executablePath)
    {
        var path = ExpectedPath(executablePath);

        if (path.Length == 0 || !File.Exists(path))
        {
            return NamecheapCredentials.Empty;
        }

        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject root)
            {
                return NamecheapCredentials.Empty;
            }

            var section = FindSection(root);
            if (section is null)
            {
                return NamecheapCredentials.Empty;
            }

            return new NamecheapCredentials(
                Value(section, "ApiKey"),
                Value(section, "UserName"),
                Value(section, "ApiUserName"),
                Value(section, "ClientIP"));
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return NamecheapCredentials.Empty;
        }
    }

    /// <summary>
    /// Writes the credentials, keeping every other key in the file. Throws on failure:
    /// unlike reading, a save the user explicitly asked for must not fail quietly.
    /// </summary>
    public static void Write(string executablePath, NamecheapCredentials credentials)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(credentials);

        var path = ExpectedPath(executablePath);

        JsonObject root;

        try
        {
            root = File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject existing
                ? existing
                : new JsonObject();
        }
        catch (JsonException)
        {
            // An unparseable file is replaced rather than blocking the save; the user
            // asked for these values to be written.
            root = new JsonObject();
        }

        var sectionKey = FindSectionKey(root) ?? SectionName;
        var section = root[sectionKey] as JsonObject ?? new JsonObject();

        Set(section, "ApiKey", credentials.ApiKey);
        Set(section, "UserName", credentials.UserName);
        Set(section, "ApiUserName", credentials.ApiUserName);
        Set(section, "ClientIP", credentials.ClientIp);

        root[sectionKey] = section;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, root.ToJsonString(WriteOptions));
    }

    private static JsonObject? FindSection(JsonObject root)
    {
        var key = FindSectionKey(root);
        return key is null ? null : root[key] as JsonObject;
    }

    private static string? FindSectionKey(JsonObject root) =>
        root.Select(pair => pair.Key)
            .FirstOrDefault(key => string.Equals(key, SectionName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Case-insensitive lookup, so either casing of a key is accepted.</summary>
    private static string Value(JsonObject section, string name)
    {
        var match = section.FirstOrDefault(
            pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase));

        // A value of the wrong JSON type is treated as absent rather than throwing.
        try
        {
            return match.Value?.GetValue<string>() ?? string.Empty;
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            return string.Empty;
        }
    }

    /// <summary>Replaces a value under whichever casing of the key already exists.</summary>
    private static void Set(JsonObject section, string name, string value)
    {
        var existingKey = section
            .Select(pair => pair.Key)
            .FirstOrDefault(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase));

        section[existingKey ?? name] = value.Trim();
    }
}
