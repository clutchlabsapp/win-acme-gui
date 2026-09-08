namespace WinAcmeGui.Core.Plugins;

/// <summary>
/// The validation plugins this GUI knows how to configure. Flags and descriptions
/// come from https://www.win-acme.com/reference/cli.
/// </summary>
public static class PluginCatalog
{
    public const string CloudflareId = "cloudflare";
    public const string Route53Id = "route53";
    public const string AzureId = "azure";
    public const string NamecheapId = "namecheap";
    public const string SelfHostingId = "selfhosting";
    public const string FileSystemId = "filesystem";

    public const string DnsChallenge = "dns-01";
    public const string HttpChallenge = "http-01";

    /// <summary>Where to get the Namecheap helper, since it has no published binary.</summary>
    public const string NamecheapHelperUrl = "https://github.com/fynydd/Fynydd.NameCheap";

    public static IReadOnlyList<ValidationPlugin> ValidationPlugins { get; } =
    [
        new ValidationPlugin
        {
            Id = CloudflareId,
            DisplayName = "Cloudflare",
            Description = "Creates the _acme-challenge TXT record through the Cloudflare API.",
            Fields =
            [
                new PluginField
                {
                    Name = "cloudflareapitoken",
                    Label = "API token",
                    Kind = PluginFieldKind.Secret,
                    Help = "A scoped token with Zone:DNS:Edit on the zone, not the global API key.",
                },
            ],
        },

        new ValidationPlugin
        {
            Id = Route53Id,
            DisplayName = "Amazon Route 53",
            Description = "Creates the TXT record in Route 53. Use the instance IAM role where you can, "
                          + "and an access key pair otherwise.",
            Fields =
            [
                new PluginField
                {
                    Name = "route53iamrole",
                    Label = "IAM role",
                    Required = false,
                    Help = "For an EC2 instance with a role attached. Leave blank to use an access key instead.",
                },
                new PluginField
                {
                    Name = "route53accesskeyid",
                    Label = "Access key ID",
                    Required = false,
                },
                new PluginField
                {
                    Name = "route53secretaccesskey",
                    Label = "Secret access key",
                    Kind = PluginFieldKind.Secret,
                    Required = false,
                },
            ],
            ExtraValidation = static values =>
                values.Has("route53iamrole")
                || (values.Has("route53accesskeyid") && values.Has("route53secretaccesskey"))
                    ? Array.Empty<string>()
                    : new[]
                    {
                        "Route 53 needs either an IAM role, or both an access key ID and a secret access key.",
                    },
        },

        new ValidationPlugin
        {
            Id = AzureId,
            DisplayName = "Azure DNS",
            Description = "Creates the TXT record in an Azure DNS zone. Authenticate with a managed "
                          + "identity, or with an app registration's client id and secret.",
            Fields =
            [
                new PluginField
                {
                    Name = "azuresubscriptionid",
                    Label = "Subscription ID",
                },
                new PluginField
                {
                    Name = "azureresourcegroupname",
                    Label = "Resource group",
                },
                new PluginField
                {
                    Name = "azurehostedzone",
                    Label = "Hosted zone",
                    Required = false,
                    Help = "Leave blank to let win-acme find the best matching zone.",
                },
                new PluginField
                {
                    Name = "azureusemsi",
                    Label = "Use managed identity",
                    Kind = PluginFieldKind.Boolean,
                    Required = false,
                    Help = "Tick this on an Azure VM with a managed identity; the fields below are then unused.",
                },
                new PluginField
                {
                    Name = "azuretenantid",
                    Label = "Tenant ID",
                    Required = false,
                },
                new PluginField
                {
                    Name = "azureclientid",
                    Label = "Client ID",
                    Required = false,
                },
                new PluginField
                {
                    Name = "azuresecret",
                    Label = "Client secret",
                    Kind = PluginFieldKind.Secret,
                    Required = false,
                },
            ],
            ExtraValidation = static values =>
                values.IsTrue("azureusemsi")
                || (values.Has("azuretenantid") && values.Has("azureclientid") && values.Has("azuresecret"))
                    ? Array.Empty<string>()
                    : new[]
                    {
                        "Azure DNS needs either a managed identity, or all of tenant ID, client ID and client secret.",
                    },
        },
        new ValidationPlugin
        {
            Id = NamecheapId,
            DisplayName = "Namecheap (via Fynydd.NameCheap)",
            Description = "Namecheap has no win-acme plugin. This drives Fynydd.NameCheap, a small "
                          + "helper program you build yourself, through win-acme's built-in script "
                          + "plugin. Its credentials live in an appsettings.json next to it, so they "
                          + "never appear on a command line.",

            // The script plugin ships inside win-acme, so unlike the others this needs
            // no plugin download and works on the trimmed build.
            RequiresSeparateDownload = false,

            ScriptWiring = new ScriptWiring
            {
                ExecutableFieldName = "namecheapexe",
                ExpectedFileName = "NameCheap.exe",

                // Matches the helper's own command line:
                //   NameCheap.exe [create|delete] [hostname] [name] [value]
                CreateArguments = "create {ZoneName} {NodeName} {Token}",
                DeleteArguments = "delete {ZoneName} {NodeName} {Token}",
            },

            Fields =
            [
                new PluginField
                {
                    Name = "namecheapexe",
                    Label = "NameCheap.exe",
                    Kind = PluginFieldKind.FilePath,
                    Help = "Built from " + NamecheapHelperUrl + ". Note the file is NameCheap.exe, not "
                           + "Fynydd.NameCheap.exe as its readme says. Keep it in the win-acme folder.",
                },
                new PluginField
                {
                    Name = "apikey",
                    Label = "API key",
                    Kind = PluginFieldKind.Secret,
                    Destination = PluginFieldDestination.ExternalFile,
                    Help = "Enable the API and get a key at namecheap.com/support/api/intro.",
                },
                new PluginField
                {
                    Name = "username",
                    Label = "Username",
                    Destination = PluginFieldDestination.ExternalFile,
                    Help = "Your Namecheap sign-in name.",
                },
                new PluginField
                {
                    Name = "apiusername",
                    Label = "API username",
                    Destination = PluginFieldDestination.ExternalFile,
                    Help = "Usually the same as the username.",
                },
                new PluginField
                {
                    Name = "clientip",
                    Label = "Whitelisted IP",
                    Destination = PluginFieldDestination.ExternalFile,
                    Help = "Must be this server's current public IP, and whitelisted in your Namecheap "
                           + "API settings. Namecheap checks the two match.",
                },
            ],
        },
        new ValidationPlugin
        {
            Id = "gcpdns",

            // Answers to --validation gcpdns but ships as plugin.validation.dns.googledns.
            DownloadName = "googledns",
            DisplayName = "Google Cloud DNS",
            Description = "Creates the TXT record in a Cloud DNS managed zone.",
            Fields =
            [
                new PluginField
                {
                    Name = "serviceaccountkey",
                    Label = "Service account key",
                    Kind = PluginFieldKind.FilePath,
                    Help = "The .json key file for a service account with the DNS Administrator role.",
                },
                new PluginField
                {
                    Name = "projectid",
                    Label = "Project ID",
                    Help = "The project that hosts the Cloud DNS zone.",
                },
            ],
        },

        new ValidationPlugin
        {
            Id = "godaddy",
            DisplayName = "GoDaddy",
            Description = "Creates the TXT record through the GoDaddy API.",
            Fields =
            [
                new PluginField { Name = "apikey", Label = "API key", Kind = PluginFieldKind.Secret },
                new PluginField { Name = "apisecret", Label = "API secret", Kind = PluginFieldKind.Secret },
            ],
        },

        new ValidationPlugin
        {
            Id = "dnsmadeeasy",
            DisplayName = "DNS Made Easy",
            Description = "Creates the TXT record through the DNS Made Easy API.",
            Fields =
            [
                new PluginField { Name = "apikey", Label = "API key", Kind = PluginFieldKind.Secret },
                new PluginField { Name = "apisecret", Label = "API secret", Kind = PluginFieldKind.Secret },
            ],
        },

        new ValidationPlugin
        {
            Id = "digitalocean",
            DisplayName = "DigitalOcean",
            Description = "Creates the TXT record through the DigitalOcean API.",
            Fields =
            [
                new PluginField
                {
                    Name = "digitaloceanapitoken",
                    Label = "API token",
                    Kind = PluginFieldKind.Secret,
                    Help = "A personal access token with write scope.",
                },
            ],
        },

        new ValidationPlugin
        {
            Id = "linode",
            DisplayName = "Linode",
            Description = "Creates the TXT record through the Linode API.",
            Fields =
            [
                new PluginField
                {
                    Name = "apitoken",
                    Label = "Personal access token",
                    Kind = PluginFieldKind.Secret,
                    Help = "Needs read/write access to Domains.",
                },
            ],
        },

        new ValidationPlugin
        {
            Id = "dreamhost",
            DisplayName = "DreamHost",
            Description = "Creates the TXT record through the DreamHost API.",
            Fields =
            [
                new PluginField { Name = "apikey", Label = "API key", Kind = PluginFieldKind.Secret },
            ],
        },

        new ValidationPlugin
        {
            Id = SelfHostingId,
            DisplayName = "Self-hosting (win-acme answers on port 80)",
            ValidationMode = HttpChallenge,
            RequiresSeparateDownload = false,
            Description = "win-acme serves the challenge itself. Nothing to configure, but the ACME "
                          + "server must be able to reach this machine on port 80 from the internet.",
            Fields =
            [
                new PluginField
                {
                    Name = "validationport",
                    Label = "Listen port",
                    Required = false,
                    Help = "Blank means 80. Only change this if something forwards port 80 here.",
                },
                new PluginField
                {
                    Name = "validationprotocol",
                    Label = "Protocol",
                    Required = false,
                    Help = "Blank means http. Set to https only if redirects happen before requests reach you.",
                },
            ],
        },

        new ValidationPlugin
        {
            Id = FileSystemId,
            DisplayName = "File system (write the challenge to a web root)",
            ValidationMode = HttpChallenge,
            RequiresSeparateDownload = false,
            Description = "Writes the challenge file into a folder your existing web server already "
                          + "serves. Nothing new listens on port 80.",
            Fields =
            [
                new PluginField
                {
                    Name = "webroot",
                    Label = "Web root",
                    Kind = PluginFieldKind.FolderPath,
                    Help = "The folder served at the site root; win-acme writes into .well-known below it.",
                },
                new PluginField
                {
                    Name = "validationsiteid",
                    Label = "IIS site ID",
                    Required = false,
                    Help = "Optional. Lets win-acme work the web root out from an IIS site instead.",
                },
                new PluginField
                {
                    Name = "manualtargetisiis",
                    Label = "Copy the default web.config",
                    Kind = PluginFieldKind.Boolean,
                    Required = false,
                    Help = "Tick on IIS, which otherwise refuses to serve the extensionless challenge file.",
                },
            ],
        },
    ];

    /// <summary>Plugins offering the given challenge, for the mode selector.</summary>
    public static IReadOnlyList<ValidationPlugin> ForChallenge(string challenge) =>
        [.. ValidationPlugins.Where(p =>
            string.Equals(p.ValidationMode, challenge, StringComparison.OrdinalIgnoreCase))];

    public static ValidationPlugin? Find(string? id) =>
        ValidationPlugins.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    public static ValidationPlugin Get(string id) =>
        Find(id) ?? throw new ArgumentException($"Unknown validation plugin '{id}'.", nameof(id));
}
