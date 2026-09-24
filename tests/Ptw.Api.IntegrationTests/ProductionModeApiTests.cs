using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ptw.Application;
using Ptw.Contracts;
using Ptw.Infrastructure;
using Ptw.Infrastructure.Security;

namespace Ptw.Api.IntegrationTests;

/// <summary>
/// Runs the API with the Production environment against the shared test database to prove the
/// production guards: login is off unless configured, requires the Portal API when on, issues a
/// Secure cookie, development identity headers are ignored, and the malware-scan switch is honoured.
/// </summary>
[Collection(PtwApiTestGroup.Name)]
public sealed class ProductionModeApiTests(PtwApiFactory factory)
{
    private const string LocalPassword = "LocalOnly12345";
    private const string DirectoryPassword = "Directory-Secret-98765";
    private static readonly Uri HttpsBase = new("https://localhost");

    [Fact]
    public async Task LoginIsFailClosedWithoutExplicitConfiguration()
    {
        using var production = Production(new Dictionary<string, string?>
        {
            // A null value removes the key that appsettings.Production.json sets, so this exercises
            // the code default rather than the shipped configuration file.
            ["Authentication:LoginEnabled"] = null
        });
        using var client = production.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = HttpsBase });

        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("anyone", "anything"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, login.StatusCode);
        var problem = await login.Content.ReadFromJsonAsync<ProblemDetailsPayload>();
        Assert.Equal("authentication.local_login_disabled", problem?.Code);
    }

    [Fact]
    public async Task DevelopmentIdentityHeadersAreRejectedInProduction()
    {
        using var production = Production(new Dictionary<string, string?>
        {
            ["Authentication:LoginEnabled"] = "false"
        });
        using var client = production.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = HttpsBase });
        client.DefaultRequestHeaders.Add("X-Dev-User", "prod.intruder");
        client.DefaultRequestHeaders.Add("X-Dev-Roles", "Administrator");

        using var me = await client.GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public void EnablingLoginWithoutPortalApiFailsStartup()
    {
        using var production = Production(new Dictionary<string, string?>
        {
            ["Authentication:LoginEnabled"] = "true",
            ["PortalAuth:BaseUrl"] = ""
        });

        var exception = Assert.ThrowsAny<Exception>(() => production.CreateClient());

        Assert.Contains("PortalAuth", FlattenMessages(exception), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PortalVerifiedLoginIssuesSecureCookieInProduction()
    {
        var (userName, subjectId) = await CreateUserAsync();
        factory.DirectoryAuthenticator.Accept(userName, DirectoryPassword);
        using var production = Production(
            new Dictionary<string, string?> { ["Authentication:LoginEnabled"] = "true" },
            services =>
            {
                // PortalAuthenticationSettings is built eagerly from builder.Configuration, so the
                // enabled portal is injected the same way the factory injects other settings.
                services.RemoveAll<PortalAuthenticationSettings>();
                services.AddSingleton(new PortalAuthenticationSettings
                {
                    Enabled = true,
                    BaseUrl = new Uri("https://portal.example.test/")
                });
            });
        using var client = production.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = HttpsBase });

        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(userName, DirectoryPassword));

        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var setCookie = Assert.Single(login.Headers.GetValues("Set-Cookie"));
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        var me = await client.GetFromJsonAsync<MeResponse>("/api/v1/me");
        Assert.Equal(subjectId, me?.UserId);
    }

    [Fact]
    public async Task ApiResponsesAreMarkedNoStore()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", $"cache.probe.{Guid.NewGuid():N}");
        client.DefaultRequestHeaders.Add("X-Dev-Roles", "Sponsor");

        using var me = await client.GetAsync("/api/v1/me");

        me.EnsureSuccessStatusCode();
        Assert.True(me.Headers.CacheControl?.NoStore, "Respons API harus membawa Cache-Control: no-store.");
    }

    [Fact]
    public async Task LoginWindowRejectsExcessAttemptsFromOneAddress()
    {
        using var limited = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimiting:LoginPermitLimit"] = "2"
                })));
        using var client = limited.CreateClient();
        var request = new LoginRequest($"nobody-{Guid.NewGuid():N}", "wrong-password");

        using var first = await client.PostAsJsonAsync("/api/v1/auth/login", request);
        using var second = await client.PostAsJsonAsync("/api/v1/auth/login", request);
        using var third = await client.PostAsJsonAsync("/api/v1/auth/login", request);

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }

    [Fact]
    public void TrustedUploadScannerFollowsTheConfiguredSwitch()
    {
        using var trusting = Production(
            new Dictionary<string, string?> { ["Authentication:LoginEnabled"] = "false" },
            services => ReplaceAttachmentSettings(services, requireMalwareScan: false));
        using var requiring = Production(
            new Dictionary<string, string?> { ["Authentication:LoginEnabled"] = "false" },
            services => ReplaceAttachmentSettings(services, requireMalwareScan: true));

        Assert.True(trusting.Services.GetRequiredService<IMalwareScanner>().IsAvailable);
        Assert.False(requiring.Services.GetRequiredService<IMalwareScanner>().IsAvailable);
    }

    private WebApplicationFactory<Program> Production(
        Dictionary<string, string?> settings,
        Action<IServiceCollection>? configureServices = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
            if (configureServices is not null)
            {
                builder.ConfigureServices(configureServices);
            }
        });

    private static void ReplaceAttachmentSettings(IServiceCollection services, bool requireMalwareScan)
    {
        services.RemoveAll<AttachmentSettings>();
        services.AddSingleton(new AttachmentSettings
        {
            Enabled = true,
            MaxFileBytes = 1048576,
            MaxFilesPerPermit = 20,
            RequireMalwareScan = requireMalwareScan,
            StoragePath = Path.Combine(Path.GetTempPath(), "ptw-online-tests", Guid.NewGuid().ToString("N"))
        });
    }

    private async Task<(string UserName, string SubjectId)> CreateUserAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var subjectId = $"prod.user.{suffix}";
        var userName = $"prod-{suffix}";
        using var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Add("X-Dev-User", $"prod.creator.{suffix}");
        admin.DefaultRequestHeaders.Add("X-Dev-Name", "Pembuat Akun");
        admin.DefaultRequestHeaders.Add("X-Dev-Roles", "Administrator");
        using var response = await admin.PostAsJsonAsync(
            "/api/v1/admin/users",
            new CreateUserRequest(subjectId, userName, "Pengguna Produksi", "Staff", "TI", LocalPassword));
        response.EnsureSuccessStatusCode();
        return (userName, subjectId);
    }

    private static string FlattenMessages(Exception exception)
    {
        var messages = new List<string>();
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" | ", messages);
    }

    private sealed record ProblemDetailsPayload(string? Code, string? Title);
}
