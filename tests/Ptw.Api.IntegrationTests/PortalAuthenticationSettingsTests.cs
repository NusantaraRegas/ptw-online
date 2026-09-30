using Microsoft.Extensions.Configuration;
using Ptw.Infrastructure.Security;

namespace Ptw.Api.IntegrationTests;

public sealed class PortalAuthenticationSettingsTests
{
    [Fact]
    public void CompleteConfigurationEnablesThePortal()
    {
        var settings = PortalAuthenticationSettings.FromConfiguration(
            Build(EnabledValues(("PortalAuth:BaseUrl", "https://portal.example.com"))),
            isDevelopment: false);

        Assert.True(settings.Enabled);
        Assert.Equal(new Uri("https://portal.example.com/"), settings.BaseUrl);
        Assert.Equal(new Uri("https://portal.example.com/api/v1/User/SecureAuth"), settings.AuthenticateEndpoint);
        Assert.Equal(new Uri("https://portal.example.com/api/v1/User/ADAuth"), settings.DirectoryAuthenticateEndpoint);
        Assert.Equal("portal-service", settings.ServiceUserName);
        Assert.Equal("service-secret", settings.ServicePassword);
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

    [Theory]
    [InlineData(null, "service-secret")]
    [InlineData("", "service-secret")]
    [InlineData("portal-service", null)]
    [InlineData("portal-service", "")]
    public void EnabledPortalRequiresServiceCredential(string? userName, string? password)
    {
        Assert.Throws<InvalidOperationException>(() => PortalAuthenticationSettings.FromConfiguration(
            Build(new Dictionary<string, string?>
            {
                ["PortalAuth:BaseUrl"] = "https://portal.example.com",
                ["PortalAuth:ServiceUserName"] = userName,
                ["PortalAuth:ServicePassword"] = password
            }),
            isDevelopment: false));
    }

    [Fact]
    public void BasePathAndCustomAuthenticatePathAreComposed()
    {
        var settings = PortalAuthenticationSettings.FromConfiguration(
            Build(EnabledValues(
                ("PortalAuth:BaseUrl", "https://portal.example.com/portal"),
                ("PortalAuth:AuthenticatePath", "/api/v2/User/SecureAuth"),
                ("PortalAuth:DirectoryAuthenticatePath", "/api/v2/User/ADAuth"),
                ("PortalAuth:TimeoutSeconds", "3"))),
            isDevelopment: false);

        Assert.Equal(new Uri("https://portal.example.com/portal/api/v2/User/SecureAuth"), settings.AuthenticateEndpoint);
        Assert.Equal(
            new Uri("https://portal.example.com/portal/api/v2/User/ADAuth"),
            settings.DirectoryAuthenticateEndpoint);
        Assert.Equal(3, settings.TimeoutSeconds);
    }

    [Fact]
    public void ClearTextBaseUrlIsOnlyAcceptedInDevelopment()
    {
        var values = EnabledValues(("PortalAuth:BaseUrl", "http://local.api.portal.com/"));

        var development = PortalAuthenticationSettings.FromConfiguration(Build(values), isDevelopment: true);
        Assert.True(development.Enabled);

        Assert.False(development.InsecureHttpAccepted);

        Assert.Throws<InvalidOperationException>(() =>
            PortalAuthenticationSettings.FromConfiguration(Build(values), isDevelopment: false));
    }

    [Fact]
    public void ClearTextBaseUrlOutsideDevelopmentRequiresExplicitOptIn()
    {
        // OPN-007 amendment: production may reach an internal-network portal over http only when the
        // operator sets the literal opt-in; anything else keeps the fail-closed default.
        var accepted = PortalAuthenticationSettings.FromConfiguration(
            Build(EnabledValues(
                ("PortalAuth:BaseUrl", "http://10.10.10.12:7100"),
                ("PortalAuth:AllowInsecureHttp", "true"))),
            isDevelopment: false);

        Assert.True(accepted.Enabled);
        Assert.True(accepted.InsecureHttpAccepted);
        Assert.Equal(new Uri("http://10.10.10.12:7100/api/v1/User/SecureAuth"), accepted.AuthenticateEndpoint);

        var httpsWithOptIn = PortalAuthenticationSettings.FromConfiguration(
            Build(EnabledValues(
                ("PortalAuth:BaseUrl", "https://portal.example.com"),
                ("PortalAuth:AllowInsecureHttp", "true"))),
            isDevelopment: false);
        Assert.False(httpsWithOptIn.InsecureHttpAccepted);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("")]
    [InlineData("yes")]
    [InlineData("1")]
    public void ClearTextBaseUrlOutsideDevelopmentRejectsNonLiteralOptIn(string allowInsecureHttp)
    {
        Assert.Throws<InvalidOperationException>(() => PortalAuthenticationSettings.FromConfiguration(
            Build(EnabledValues(
                ("PortalAuth:BaseUrl", "http://10.10.10.12:7100"),
                ("PortalAuth:AllowInsecureHttp", allowInsecureHttp))),
            isDevelopment: false));
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("ldap://10.0.0.1")]
    [InlineData("/relative/only")]
    public void InvalidBaseUrlIsRejected(string baseUrl)
    {
        Assert.Throws<InvalidOperationException>(() => PortalAuthenticationSettings.FromConfiguration(
            Build(EnabledValues(("PortalAuth:BaseUrl", baseUrl))),
            isDevelopment: true));
    }

    [Fact]
    public void AbsoluteAuthenticatePathIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => PortalAuthenticationSettings.FromConfiguration(
            Build(EnabledValues(
                ("PortalAuth:BaseUrl", "https://portal.example.com"),
                ("PortalAuth:AuthenticatePath", "https://elsewhere.example.com/SecureAuth"))),
            isDevelopment: true));
    }

    [Fact]
    public void AbsoluteDirectoryAuthenticatePathIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => PortalAuthenticationSettings.FromConfiguration(
            Build(EnabledValues(
                ("PortalAuth:BaseUrl", "https://portal.example.com"),
                ("PortalAuth:DirectoryAuthenticatePath", "https://elsewhere.example.com/ADAuth"))),
            isDevelopment: true));
    }

    [Fact]
    public void ShippedDevelopmentConfigurationKeepsPortalDisabledUntilSecretsAreInjected()
    {
        // No service credential belongs in a tracked appsettings file. Compose/user-secrets inject the
        // complete PortalAuth configuration when directory login is wanted in Development.
        var configuration = new ConfigurationBuilder()
            .SetBasePath(FindApiProjectDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: false)
            .Build();

        var settings = PortalAuthenticationSettings.FromConfiguration(configuration, isDevelopment: true);

        Assert.False(settings.Enabled);
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

    private static Dictionary<string, string?> EnabledValues(
        params (string Key, string? Value)[] additionalValues)
    {
        var values = new Dictionary<string, string?>
        {
            ["PortalAuth:ServiceUserName"] = "portal-service",
            ["PortalAuth:ServicePassword"] = "service-secret"
        };
        foreach (var (key, value) in additionalValues)
        {
            values[key] = value;
        }

        return values;
    }

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
