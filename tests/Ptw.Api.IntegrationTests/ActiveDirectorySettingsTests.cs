using Microsoft.Extensions.Configuration;
using Ptw.Infrastructure.Security;

namespace Ptw.Api.IntegrationTests;

public sealed class ActiveDirectorySettingsTests
{
    [Fact]
    public void ServerAddressAloneEnablesTheDirectory()
    {
        var settings = ActiveDirectorySettings.FromConfiguration(Build(new Dictionary<string, string?>
        {
            ["AD:Server"] = "10.0.0.1",
            ["AD:Domain"] = "example.com",
            ["AD:Port"] = "389"
        }));

        Assert.True(settings.Enabled);
        Assert.Equal("10.0.0.1", settings.Server);
        Assert.Equal("example.com", settings.Domain);
        Assert.Equal(389, settings.Port);
    }

    [Fact]
    public void MissingServerDisablesTheDirectory()
    {
        var settings = ActiveDirectorySettings.FromConfiguration(Build(new Dictionary<string, string?>()));

        Assert.False(settings.Enabled);
    }

    [Fact]
    public void ExplicitEnabledFalseWinsOverServerAddress()
    {
        var settings = ActiveDirectorySettings.FromConfiguration(Build(new Dictionary<string, string?>
        {
            ["AD:Enabled"] = "false",
            ["AD:Server"] = "10.0.0.1",
            ["AD:Domain"] = "example.com"
        }));

        Assert.False(settings.Enabled);
    }

    [Fact]
    public void EnabledDirectoryRequiresDomain()
    {
        Assert.Throws<InvalidOperationException>(() => ActiveDirectorySettings.FromConfiguration(Build(
            new Dictionary<string, string?> { ["AD:Server"] = "10.0.0.1" })));
    }

    [Fact]
    public void ShippedDevelopmentConfigurationEnablesTheDirectory()
    {
        // Regression: a base appsettings.json that pins AD:Enabled=false silently overrides a
        // Development file that only fills Server/Domain/Port, so the layered result is asserted here.
        var apiDirectory = FindApiProjectDirectory();
        var configuration = new ConfigurationBuilder()
            .SetBasePath(apiDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: false)
            .Build();

        var settings = ActiveDirectorySettings.FromConfiguration(configuration);

        Assert.True(settings.Enabled);
        Assert.False(string.IsNullOrWhiteSpace(settings.Server));
        Assert.False(string.IsNullOrWhiteSpace(settings.Domain));
    }

    [Fact]
    public void ShippedBaseConfigurationAloneKeepsTheDirectoryDisabled()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(FindApiProjectDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        Assert.False(ActiveDirectorySettings.FromConfiguration(configuration).Enabled);
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
