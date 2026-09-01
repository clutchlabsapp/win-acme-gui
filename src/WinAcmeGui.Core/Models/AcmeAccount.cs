namespace WinAcmeGui.Core.Models;

/// <summary>
/// The ACME account details win-acme needs to register or reuse an account.
/// </summary>
public sealed class AcmeAccount
{
    /// <summary>Passed as <c>--emailaddress</c>. Let's Encrypt uses it for expiry warnings.</summary>
    public string EmailAddress { get; set; } = string.Empty;

    /// <summary>Emits <c>--accepttos</c>. Without it win-acme stops and asks.</summary>
    public bool AcceptTermsOfService { get; set; }

    /// <summary>
    /// Emits <c>--test</c>, which points win-acme at the Let's Encrypt staging
    /// endpoint. Staging certificates are not trusted but are not rate limited,
    /// so this is the safe way to prove a configuration works.
    /// </summary>
    public bool UseTestServer { get; set; }
}
