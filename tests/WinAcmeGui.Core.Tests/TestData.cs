using WinAcmeGui.Core.Models;
using WinAcmeGui.Core.Plugins;

namespace WinAcmeGui.Core.Tests;

internal static class TestData
{
    public const string WacsPath = @"C:\win-acme\wacs.exe";

    /// <summary>A definition that passes validation, for tests to mutate one field of.</summary>
    public static RenewalDefinition ValidCloudflareDefinition() => new()
    {
        Account = new AcmeAccount
        {
            EmailAddress = "admin@example.com",
            AcceptTermsOfService = true,
        },
        Certificate = new CertificateRequest
        {
            FriendlyName = "Example cert",
            CommonName = "example.com",
            HostNames = ["example.com", "www.example.com"],
        },
        Validation = new ValidationSettings
        {
            PluginId = PluginCatalog.CloudflareId,
            Values = { ["cloudflareapitoken"] = "cf-token" },
        },
    };
}
