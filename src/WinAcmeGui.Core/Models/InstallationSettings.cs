namespace WinAcmeGui.Core.Models;

/// <summary>
/// What happens after a certificate is issued. win-acme accepts a comma-separated
/// list of installation plugins, but only one script, so this models the two things
/// that can be combined: IIS bindings, and one post-renewal script.
/// </summary>
public sealed class InstallationSettings
{
    /// <summary>Adds <c>iis</c> to <c>--installation</c>, rebinding HTTPS to the new certificate.</summary>
    public bool UpdateIisBindings { get; set; }

    /// <summary>Emits <c>--installationsiteid</c>. Blank means the source site.</summary>
    public string IisSiteId { get; set; } = string.Empty;

    /// <summary>Emits <c>--sslport</c>. Blank means 443.</summary>
    public string SslPort { get; set; } = string.Empty;

    /// <summary>Emits <c>--sslipaddress</c>. Blank means all addresses.</summary>
    public string SslIpAddress { get; set; } = string.Empty;

    /// <summary>Which script preset to run, or <c>none</c>.</summary>
    public string ScriptPresetId { get; set; } = "none";

    /// <summary>Only used by the custom preset; the bundled ones resolve their own path.</summary>
    public string ScriptPath { get; set; } = string.Empty;

    /// <summary>Overrides the preset's default parameters when set.</summary>
    public string ScriptParameters { get; set; } = string.Empty;
}
