using Microsoft.Extensions.Configuration;
using Ptw.Infrastructure.Security;

namespace Ptw.Api.IntegrationTests;

public sealed class PortalAuthenticationSettingsTests
{
    [Fact]
    public void BaseUrlAloneEnablesThePortal()
    {
        var settings = PortalAuthenticationSettings.FromConfiguration(
            Build(new Dictionary<string, string?> { ["PortalAuth:BaseUrl"] = "https://portal.example.com" }),
            isDevelopment: false);

        Assert.True(settings.Enabled);
        Assert.Equal(new Uri("https://portal.example.com/"), settings.BaseUrl);
        Assert.Equal(new Uri("https://portal.example.com/api/v1/User/SecureAuth"), settings.AuthenticateEndpoint);
        Assert.Equal(10, settings.TimeoutSeconds);
    }

    [Fact]
    public void MissingBaseUrlDisablesThePortal()
    {
        var settings = PortalAuthenticationSettings.FromConfiguration(
            Build(new Dictionary<string, string?>()),
            isDevelopment: true);

        Assert.False(settings.Enabled);
        Assert.Null(settings.AuthenticateEndpoint);
    }

    [Fact]
    public void ExplicitEnabledFalseWinsOverBaseUrl()
    {
        var settings = PortalAuthenticationSettings.FromConfiguration(
            Build(new Dictionary<string, string?>
            {
                ["PortalAuth:Enabled"] = "false",
                ["PortalAuth:BaseUrl"] = "https://portal.example.com"
            }),
            isDevelopment: true);

        Assert.False(settings.Enabled);
    }

    [Fact]
    public void BasePathAndCustomAuthenticatePathAreComposed()
    {
        var settings = PortalAuthenticationSettings.FromConfiguration(
            Build(new Dictionary<string, string?>
            {
                ["PortalAuth:BaseUrl"] = "https://portal.example.com/portal",
                ["PortalAuth:AuthenticatePath"] = "/api/v2/User/SecureAuth",
                ["PortalAuth:TimeoutSeconds"] = "3"
            }),
            isDevelopment: false);

        Assert.Equal(new Uri("https://portal.example.com/portal/api/v2/User/SecureAuth"), settings.AuthenticateEndpoint);
        Assert.Equal(3, settings.TimeoutSeconds);
    }

    [Fact]
    public void ClearTextBaseUrlIsOnlyAcceptedInDevelopment()
    {
        var values = new Dictionary<string, string?> { ["PortalAuth:BaseUrl"] = "http://local.api.portal.com/" };

        var development = PortalAuthenticationSettings.FromConfiguration(Build(values), isDevelopment: true);
        Assert.True(development.Enabled);

        Assert.Throws<InvalidOperationException>(() =>
            PortalAuthenticationSettings.FromConfiguration(Build(values), isDevelopment: false));
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("ldap://10.0.0.1")]
    [InlineData("/relative/only")]
    public void InvalidBaseUrlIsRejected(string baseUrl)
    {
        Assert.Throws<InvalidOperationException>(() => PortalAuthenticationSettings.FromConfiguration(
            Build(new Dictionary<string, string?> { ["PortalAuth:BaseUrl"] = baseUrl }),
            isDevelopment: true));
    }

    [Fact]
    public void AbsoluteAuthenticatePathIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => PortalAuthenticationSettings.FromConfiguration(
            Build(new Dictionary<string, string?>
            {
                ["PortalAuth:BaseUrl"] = "https://portal.example.com",
                ["PortalAuth:AuthenticatePath"] = "https://elsewhere.example.com/SecureAuth"
            }),
            isDevelopment: true));
    }

    [Fact]
    public void ShippedDevelopmentConfigurationEnablesThePortal()
    {
        // Regression: a base appsettings.json that pins PortalAuth:Enabled=false would silently override
        // a Development file that only fills BaseUrl, so the layered result is asserted here.
        var configuration = new ConfigurationBuilder()
            .SetBasePath(FindApiProjectDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: false)
            .Build();

        var settings = PortalAuthenticationSettings.FromConfiguration(configuration, isDevelopment: true);

        Assert.True(settings.Enabled);
        Assert.NotNull(settings.AuthenticateEndpoint);
        Assert.EndsWith("/api/v1/User/SecureAuth", settings.AuthenticateEndpoint.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public void ShippedBaseConfigurationAloneKeepsThePortalDisabled()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(FindApiProjectDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        Assert.False(PortalAuthenticationSettings.FromConfiguration(configuration, isDevelopment: false).Enabled);
    }

    private static IConfiguration Build(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static string FindApiProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Ptw.Api", "appsettings.json");
            if (File.Exists(candidate))
            {
                return Path.GetDirectoryName(candidate)!;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("src/Ptw.Api/appsettings.json tidak ditemukan dari direktori test.");
    }
}
