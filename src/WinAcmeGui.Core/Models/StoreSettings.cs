namespace WinAcmeGui.Core.Models;

/// <summary>
/// Where the issued certificate is saved. Phase 1 only exposes the Windows
/// certificate store, which is what both IIS and the RDP listener read from.
/// </summary>
public sealed class StoreSettings
{
    /// <summary>Emits <c>--store</c>.</summary>
    public string PluginId { get; set; } = "certificatestore";

    /// <summary>
    /// Emits <c>--certificatestore</c>. Blank leaves win-acme's own default, which is
    /// <c>WebHosting</c> on modern Windows. Set it to <c>My</c> for the RDP scripts,
    /// which only look in <c>LocalMachine\My</c>.
    /// </summary>
    public string StoreName { get; set; } = string.Empty;

    /// <summary>Emits <c>--keepexisting</c>: leave the previous certificate in place on renewal.</summary>
    public bool KeepExisting { get; set; }
}
