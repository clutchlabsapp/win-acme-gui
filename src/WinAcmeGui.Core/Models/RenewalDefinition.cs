namespace WinAcmeGui.Core.Models;

/// <summary>
/// Everything the GUI collects for one renewal, in one place. This is what gets
/// turned into a wacs.exe command line.
/// </summary>
public sealed class RenewalDefinition
{
    public AcmeAccount Account { get; set; } = new();

    public CertificateRequest Certificate { get; set; } = new();

    public ValidationSettings Validation { get; set; } = new();

    public StoreSettings Store { get; set; } = new();
}
