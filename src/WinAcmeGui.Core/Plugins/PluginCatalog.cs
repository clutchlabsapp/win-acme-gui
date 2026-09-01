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
    ];

    public static ValidationPlugin? Find(string? id) =>
        ValidationPlugins.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    public static ValidationPlugin Get(string id) =>
        Find(id) ?? throw new ArgumentException($"Unknown validation plugin '{id}'.", nameof(id));
}
