using WinAcmeGui.Core;
using WinAcmeGui.Core.Models;
using WinAcmeGui.Core.Plugins;
using Xunit;

namespace WinAcmeGui.Core.Tests;

public class RenewalValidatorTests
{
    [Fact]
    public void AWellFormedDefinitionHasNoProblems()
    {
        Assert.Empty(RenewalValidator.Validate(TestData.ValidCloudflareDefinition()));
        Assert.True(RenewalValidator.IsValid(TestData.ValidCloudflareDefinition()));
    }

    [Fact]
    public void RequiresAtLeastOneHostName()
    {
        var definition = TestData.ValidCloudflareDefinition();
        definition.Certificate.HostNames.Clear();
        definition.Certificate.CommonName = string.Empty;

        Assert.Contains(
            RenewalValidator.Validate(definition),
            problem => problem.Contains("at least one host name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RejectsAMalformedHostName()
    {
        var definition = TestData.ValidCloudflareDefinition();
        definition.Certificate.HostNames = ["example.com", "not a host"];

        Assert.Contains(
            RenewalValidator.Validate(definition),
            problem => problem.Contains("not a host", StringComparison.Ordinal));
    }

    [Fact]
    public void CommonNameMustBeOneOfTheHostNames()
    {
        var definition = TestData.ValidCloudflareDefinition();
        definition.Certificate.CommonName = "other.example.com";

        Assert.Contains(
            RenewalValidator.Validate(definition),
            problem => problem.Contains("must also appear", StringComparison.Ordinal));
    }

    [Fact]
    public void ABlankCommonNameIsFineBecauseWinAcmeDefaultsToTheFirstHost()
    {
        var definition = TestData.ValidCloudflareDefinition();
        definition.Certificate.CommonName = string.Empty;

        Assert.Empty(RenewalValidator.Validate(definition));
    }

    [Fact]
    public void TermsOfServiceMustBeAccepted()
    {
        var definition = TestData.ValidCloudflareDefinition();
        definition.Account.AcceptTermsOfService = false;

        Assert.Contains(
            RenewalValidator.Validate(definition),
            problem => problem.Contains("terms of service", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("two@at@example.com")]
    [InlineData("nodomain@localhost")]
    [InlineData("spaces in@example.com")]
    public void RejectsAnUnusableEmailAddress(string email)
    {
        var definition = TestData.ValidCloudflareDefinition();
        definition.Account.EmailAddress = email;

        Assert.NotEmpty(RenewalValidator.Validate(definition));
    }

    [Fact]
    public void CloudflareNeedsItsApiToken()
    {
        var definition = TestData.ValidCloudflareDefinition();
        definition.Validation.Values.Clear();

        Assert.Contains(
            RenewalValidator.Validate(definition),
            problem => problem.Contains("API token", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AnUnknownPluginIsReported()
    {
        var definition = TestData.ValidCloudflareDefinition();
        definition.Validation = new ValidationSettings { PluginId = "nope" };

        Assert.Contains(
            RenewalValidator.Validate(definition),
            problem => problem.Contains("validation plugin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Route53AcceptsAnIamRoleOnItsOwn()
    {
        var definition = TestData.ValidCloudflareDefinition();
        definition.Validation = new ValidationSettings
        {
            PluginId = PluginCatalog.Route53Id,
            Values = { ["route53iamrole"] = "cert-renewal" },
        };

        Assert.Empty(RenewalValidator.Validate(definition));
    }

    [Fact]
    public void Route53RejectsHalfOfAnAccessKeyPair()
    {
        var definition = TestData.ValidCloudflareDefinition();
        definition.Validation = new ValidationSettings
        {
            PluginId = PluginCatalog.Route53Id,
            Values = { ["route53accesskeyid"] = "AKIAEXAMPLE" },
        };

        Assert.Contains(
            RenewalValidator.Validate(definition),
            problem => problem.Contains("Route 53", StringComparison.Ordinal));
    }

    [Fact]
    public void AzureAcceptsAManagedIdentityWithoutClientCredentials()
    {
        var definition = TestData.ValidCloudflareDefinition();
        definition.Validation = new ValidationSettings
        {
            PluginId = PluginCatalog.AzureId,
            Values =
            {
                ["azuresubscriptionid"] = "sub-1",
                ["azureresourcegroupname"] = "rg-dns",
                ["azureusemsi"] = "true",
            },
        };

        Assert.Empty(RenewalValidator.Validate(definition));
    }

    [Fact]
    public void AzureWithoutManagedIdentityNeedsAllThreeClientFields()
    {
        var definition = TestData.ValidCloudflareDefinition();
        definition.Validation = new ValidationSettings
        {
            PluginId = PluginCatalog.AzureId,
            Values =
            {
                ["azuresubscriptionid"] = "sub-1",
                ["azureresourcegroupname"] = "rg-dns",
                ["azuretenantid"] = "tenant",
                ["azureclientid"] = "client",
            },
        };

        Assert.Contains(
            RenewalValidator.Validate(definition),
            problem => problem.Contains("Azure DNS", StringComparison.Ordinal));
    }

    [Fact]
    public void AzureStillRequiresSubscriptionAndResourceGroup()
    {
        var definition = TestData.ValidCloudflareDefinition();
        definition.Validation = new ValidationSettings
        {
            PluginId = PluginCatalog.AzureId,
            Values = { ["azureusemsi"] = "true" },
        };

        var problems = RenewalValidator.Validate(definition);

        Assert.Contains(problems, p => p.Contains("Subscription ID", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("Resource group", StringComparison.Ordinal));
    }
}
