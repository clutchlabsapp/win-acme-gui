namespace WinAcmeGui.Core.Models;

/// <summary>
/// What the certificate should cover. Maps to win-acme's <c>manual</c> source plugin.
/// </summary>
public sealed class CertificateRequest
{
    /// <summary>Emits <c>--friendlyname</c>. Also how the renewal is identified in <c>--list</c>.</summary>
    public string FriendlyName { get; set; } = string.Empty;

    /// <summary>
    /// Emits <c>--commonname</c>. Optional: win-acme defaults to the first host name.
    /// When set it must be one of <see cref="HostNames"/>.
    /// </summary>
    public string CommonName { get; set; } = string.Empty;

    /// <summary>Emits <c>--host</c> as a comma-separated list.</summary>
    public List<string> HostNames { get; set; } = [];
}
