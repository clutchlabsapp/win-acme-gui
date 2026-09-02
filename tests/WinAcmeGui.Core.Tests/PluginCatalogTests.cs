using WinAcmeGui.Core.Plugins;
using Xunit;

namespace WinAcmeGui.Core.Tests;

public class PluginCatalogTests
{
    [Fact]
    public void EveryPluginHasAUniqueIdAndAtLeastOneField()
    {
        var ids = PluginCatalog.ValidationPlugins.Select(p => p.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(PluginCatalog.ValidationPlugins, plugin => Assert.NotEmpty(plugin.Fields));
    }

    [Fact]
    public void FieldNamesAreFlagsWithoutLeadingDashes()
    {
        var fields = PluginCatalog.ValidationPlugins.SelectMany(p => p.Fields);

        Assert.All(fields, field =>
        {
            Assert.False(field.Name.StartsWith('-'), $"{field.Name} should not include its dashes.");
            Assert.Equal("--" + field.Name, field.Flag);
            Assert.NotEmpty(field.Label);
        });
    }

    [Fact]
    public void CredentialFieldsAreMarkedSecret()
    {
        Assert.True(PluginCatalog.Get(PluginCatalog.CloudflareId).FindField("cloudflareapitoken")!.IsSecret);
        Assert.True(PluginCatalog.Get(PluginCatalog.Route53Id).FindField("route53secretaccesskey")!.IsSecret);
        Assert.True(PluginCatalog.Get(PluginCatalog.AzureId).FindField("azuresecret")!.IsSecret);

        Assert.False(PluginCatalog.Get(PluginCatalog.Route53Id).FindField("route53accesskeyid")!.IsSecret);
    }

    [Fact]
    public void EveryPluginUsesDnsValidation()
    {
        Assert.All(PluginCatalog.ValidationPlugins, p => Assert.Equal("dns-01", p.ValidationMode));
    }

    [Fact]
    public void LookupIsCaseInsensitiveAndGetThrowsOnUnknownIds()
    {
        Assert.NotNull(PluginCatalog.Find("CloudFlare"));
        Assert.Null(PluginCatalog.Find("does-not-exist"));
        Assert.Throws<ArgumentException>(() => PluginCatalog.Get("does-not-exist"));
    }
}
