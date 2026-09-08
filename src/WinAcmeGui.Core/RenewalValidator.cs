using WinAcmeGui.Core.Models;
using WinAcmeGui.Core.Plugins;

namespace WinAcmeGui.Core;

/// <summary>
/// Catches the mistakes that would otherwise show up as a confusing wacs.exe error
/// several seconds into a run, or worse, as a renewal that silently never works.
/// </summary>
public static class RenewalValidator
{
    public static IReadOnlyList<string> Validate(RenewalDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var problems = new List<string>();

        ValidateCertificate(definition.Certificate, problems);
        ValidateAccount(definition.Account, problems);
        ValidateValidation(definition.Validation, problems);
        ValidateChallengeSuitsHostNames(definition, problems);
        ValidateInstallation(definition.Installation, definition.Store, problems);
        ValidateStore(definition.Store, problems);

        return problems;
    }

    public static bool IsValid(RenewalDefinition definition) => Validate(definition).Count == 0;

    private static void ValidateCertificate(CertificateRequest certificate, List<string> problems)
    {
        if (certificate.HostNames.Count == 0)
        {
            problems.Add("Add at least one host name.");
            return;
        }

        foreach (var host in certificate.HostNames.Where(h => !HostNameParser.IsValid(h)))
        {
            problems.Add($"'{host}' is not a valid host name.");
        }

        var commonName = HostNameParser.Normalize(certificate.CommonName);
        if (commonName.Length > 0
            && !certificate.HostNames.Any(h => string.Equals(
                HostNameParser.Normalize(h), commonName, StringComparison.OrdinalIgnoreCase)))
        {
            problems.Add($"The common name '{commonName}' must also appear in the host names.");
        }
    }

    private static void ValidateAccount(AcmeAccount account, List<string> problems)
    {
        if (!account.AcceptTermsOfService)
        {
            problems.Add("You have to accept the ACME terms of service.");
        }

        var email = account.EmailAddress.Trim();
        if (email.Length == 0)
        {
            problems.Add("Enter an email address for the ACME account.");
        }
        else if (!IsPlausibleEmail(email))
        {
            problems.Add($"'{email}' does not look like an email address.");
        }
    }

    private static void ValidateValidation(ValidationSettings validation, List<string> problems)
    {
        var plugin = PluginCatalog.Find(validation.PluginId);
        if (plugin is null)
        {
            problems.Add("Choose a DNS validation plugin.");
            return;
        }

        foreach (var field in plugin.Fields.Where(f => f.Required && f.Kind != PluginFieldKind.Boolean))
        {
            if (string.IsNullOrWhiteSpace(validation[field.Name]))
            {
                problems.Add($"{plugin.DisplayName}: {field.Label} is required.");
            }
        }

        // Whether the helper is actually on disk, and in a folder win-acme can run it
        // from, is checked in the UI: it needs the filesystem and the wacs.exe path.

        if (plugin.ExtraValidation is not null)
        {
            problems.AddRange(plugin.ExtraValidation(new ValuesLookup(validation.Values)));
        }
    }

    /// <summary>
    /// Let's Encrypt only issues wildcards over dns-01. Picking HTTP validation for a
    /// wildcard fails when the order is submitted, well after everything looks fine.
    /// </summary>
    private static void ValidateChallengeSuitsHostNames(RenewalDefinition definition, List<string> problems)
    {
        var plugin = PluginCatalog.Find(definition.Validation.PluginId);
        if (plugin is null || !string.Equals(plugin.ValidationMode, PluginCatalog.HttpChallenge, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var wildcards = definition.Certificate.HostNames.Where(HostNameParser.IsWildcard).ToList();
        if (wildcards.Count > 0)
        {
            problems.Add(
                $"{string.Join(", ", wildcards)} needs DNS validation. Wildcard certificates cannot be "
                + "issued with an HTTP challenge.");
        }
    }

    private static void ValidateStore(StoreSettings store, List<string> problems)
    {
        if (store.ExportsFiles && string.IsNullOrWhiteSpace(store.ExportFolder))
        {
            problems.Add("Choose a folder to export the certificate files to.");
        }
    }

    private static void ValidateInstallation(
        InstallationSettings installation,
        StoreSettings store,
        List<string> problems)
    {
        var preset = InstallationPresets.Find(installation.ScriptPresetId);
        if (preset is null)
        {
            problems.Add("Choose a post-renewal script, or 'No script'.");
            return;
        }

        if (preset.IsCustom && string.IsNullOrWhiteSpace(installation.ScriptPath))
        {
            problems.Add("Choose the script to run after renewal, or select 'No script'.");
        }

        // The RD and Exchange scripts read LocalMachine\My. Leaving the certificate in
        // WebHosting makes them fail silently, which is the worst possible outcome for
        // something that only runs once every 60 days.
        if (preset.RequiredStoreName.Length > 0
            && !string.Equals(store.StoreName.Trim(), preset.RequiredStoreName, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add($"{preset.DisplayName} reads certificates from LocalMachine\\{preset.RequiredStoreName}. "
                         + $"Set the certificate store to '{preset.RequiredStoreName}'.");
        }

        if (installation.UpdateIisBindings)
        {
            ValidateOptionalNumber(installation.IisSiteId, "IIS site ID", problems);
            ValidateOptionalNumber(installation.SslPort, "HTTPS port", problems);
        }
    }

    private static void ValidateOptionalNumber(string value, string label, List<string> problems)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        if (!int.TryParse(trimmed, out var parsed))
        {
            problems.Add($"{label} must be a number.");
        }
        else if (parsed <= 0)
        {
            problems.Add($"{label} must be greater than zero.");
        }
    }

    /// <summary>
    /// Deliberately loose. The ACME server is the real judge of an address; this only
    /// catches an obviously empty or malformed entry.
    /// </summary>
    private static bool IsPlausibleEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0 || at != email.LastIndexOf('@') || at == email.Length - 1)
        {
            return false;
        }

        var domain = email[(at + 1)..];
        return domain.Contains('.') && !domain.StartsWith('.') && !domain.EndsWith('.')
               && !email.Any(char.IsWhiteSpace);
    }
}
