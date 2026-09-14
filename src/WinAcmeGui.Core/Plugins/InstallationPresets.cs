namespace WinAcmeGui.Core.Plugins;

/// <summary>
/// A post-renewal script, either one of the .ps1 files that ships in win-acme's own
/// Scripts folder or one of your own.
/// </summary>
public sealed class InstallationScriptPreset
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>File name inside win-acme's Scripts folder. Empty for the custom preset.</summary>
    public string ScriptFileName { get; init; } = string.Empty;

    /// <summary>What to pass to the script when the user has not overridden it.</summary>
    public string DefaultParameters { get; init; } = string.Empty;

    /// <summary>
    /// A certificate store this script needs. The RD scripts only search
    /// <c>LocalMachine\My</c>, so leaving the certificate in WebHosting silently does
    /// nothing.
    /// </summary>
    public string RequiredStoreName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public bool IsNone => Id == InstallationPresets.NoneId;

    public bool IsCustom => Id == InstallationPresets.CustomId;

    /// <summary>
    /// True when the script ships with this tool rather than with win-acme, so it has
    /// to be written to disk before a renewal can run it.
    /// </summary>
    public bool ProvidedByTool { get; init; }
}

/// <summary>
/// The post-renewal hooks the GUI offers. Everything here is expressed as
/// <c>--installation script</c> plus a path and parameters; nothing is special-cased
/// in win-acme.
/// </summary>
public static class InstallationPresets
{
    public const string NoneId = "none";
    public const string CustomId = "custom";

    /// <summary>The bundled win-acme RDP listener script.</summary>
    public const string RdListenerBundledId = "rdlistener";

    /// <summary>This tool's own RDP listener script.</summary>
    public const string RdListenerToolId = "rdlistener-gui";

    /// <summary>Tokens win-acme substitutes into --scriptparameters.</summary>
    public static IReadOnlyList<string> ParameterTokens { get; } =
    [
        "{CertThumbprint}",
        "{CertCommonName}",
        "{CertFriendlyName}",
        "{CacheFile}",
        "{CachePassword}",
        "{CacheFolder}",
        "{StorePath}",
        "{StoreType}",
        "{RenewalId}",
        "{OldCertThumbprint}",
    ];

    public static IReadOnlyList<InstallationScriptPreset> Scripts { get; } =
    [
        new InstallationScriptPreset
        {
            Id = NoneId,
            DisplayName = "No script",
            Description = "Only save the certificate to the store.",
        },
        new InstallationScriptPreset
        {
            Id = "rdlistener",
            DisplayName = "Remote Desktop (RDP listener)",
            ScriptFileName = "ImportRDListener.ps1",
            DefaultParameters = "{CertThumbprint}",
            RequiredStoreName = "My",
            Description = "Binds the new certificate to the RDP listener on this machine.",
        },
        new InstallationScriptPreset
        {
            Id = InstallationPresets.RdListenerToolId,
            DisplayName = "Remote Desktop listener (this tool's script)",
            ScriptFileName = InstallScriptWriter.RdpListenerScriptName,
            ProvidedByTool = true,

            // Named parameters, unlike the bundled script's positional one.
            DefaultParameters = "-Thumbprint '{CertThumbprint}' -CacheFile '{CacheFile}' "
                                + "-CachePassword '{CachePassword}'",

            // No RequiredStoreName: this script copies the certificate into
            // LocalMachine\My itself, and falls back to importing win-acme's cached
            // .pfx when no Windows store was used at all.
            Description = "Same job as the bundled script, but it reports failure to win-acme instead "
                          + "of exiting zero, verifies the binding afterwards, and works even when the "
                          + "certificate was never put in the Windows store.",
        },

        new InstallationScriptPreset
        {
            Id = "rdgateway",
            DisplayName = "Remote Desktop Gateway",
            ScriptFileName = "ImportRDGateway.ps1",
            DefaultParameters = "{CertThumbprint}",
            RequiredStoreName = "My",
            Description = "Updates the RD Gateway SSL binding.",
        },
        new InstallationScriptPreset
        {
            Id = "rds",
            DisplayName = "Remote Desktop Services (listener and gateway)",
            ScriptFileName = "ImportRDS.ps1",
            DefaultParameters = "{CertThumbprint}",
            RequiredStoreName = "My",
            Description = "Updates both the RDP listener and the RD Gateway.",
        },
        new InstallationScriptPreset
        {
            Id = "exchange",
            DisplayName = "Exchange services",
            ScriptFileName = "ImportExchange.ps1",
            DefaultParameters = "{CertThumbprint} IIS,SMTP 1",
            RequiredStoreName = "My",
            Description = "Imports into Exchange. win-acme ships this script marked incomplete, "
                          + "so check the services list and test it before relying on it.",
        },
        new InstallationScriptPreset
        {
            Id = CustomId,
            DisplayName = "Custom script",
            Description = "Any .bat, .ps1 or .exe. Use the tokens below to pass certificate details to it.",
        },
    ];

    public static InstallationScriptPreset? Find(string? id) =>
        Scripts.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

    public static InstallationScriptPreset None => Scripts[0];
}
