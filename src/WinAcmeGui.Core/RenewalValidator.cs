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

        if (plugin.ExtraValidation is not null)
        {
            problems.AddRange(plugin.ExtraValidation(new ValuesLookup(validation.Values)));
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
