using System.Text.Json;
using WinAcmeGui.Core.Models;
using WinAcmeGui.Core.Plugins;

namespace WinAcmeGui;

/// <summary>
/// The handful of non-secret fields worth remembering between sessions.
/// </summary>
/// <remarks>
/// API tokens and passwords are deliberately never written here. win-acme keeps
/// credentials in its own renewal record, subject to its EncryptConfig setting,
/// which is the right place for them; this file sits unprotected in the user's
/// profile and would be a step backwards.
/// </remarks>
public sealed class AppSettings
{
    public string WacsPath { get; set; } = string.Empty;

    public string EmailAddress { get; set; } = string.Empty;

    public bool AcceptTermsOfService { get; set; }

    public bool UseTestServer { get; set; }

    public string FriendlyName { get; set; } = string.Empty;

    public string CommonName { get; set; } = string.Empty;

    public List<string> HostNames { get; set; } = [];

    public string ValidationPluginId { get; set; } = PluginCatalog.CloudflareId;

    /// <summary>Plugin field values, with every secret field left out.</summary>
    public Dictionary<string, string> ValidationValues { get; set; } = new();

    public string CertificateStoreName { get; set; } = string.Empty;

    public bool KeepExisting { get; set; }

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "win-acme-gui",
        "settings.json");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Never throws: a corrupt or unreadable settings file just means defaults.</summary>
    public static AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), SerializerOptions) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    /// <summary>Best effort. Failing to save preferences must not interrupt a renewal.</summary>
    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, SerializerOptions));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Preferences are a convenience; losing them is not worth a dialog.
        }
    }

    /// <summary>
    /// Copies the plugin values across, dropping anything the catalog marks secret so
    /// credentials never reach disk.
    /// </summary>
    public void RememberValidationValues(ValidationSettings validation)
    {
        ValidationPluginId = validation.PluginId;
        ValidationValues = new();

        var plugin = PluginCatalog.Find(validation.PluginId);
        if (plugin is null)
        {
            return;
        }

        foreach (var field in plugin.Fields.Where(f => !f.IsSecret))
        {
            var value = validation[field.Name];
            if (!string.IsNullOrWhiteSpace(value))
            {
                ValidationValues[field.Name] = value;
            }
        }
    }
}
