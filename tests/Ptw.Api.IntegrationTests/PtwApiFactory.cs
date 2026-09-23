using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ptw.Application;
using Ptw.Infrastructure.Persistence;
using Testcontainers.MsSql;

namespace Ptw.Api.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class PtwApiTestGroup : ICollectionFixture<PtwApiFactory>
{
    public const string Name = "PTW API integration";
}

public sealed class PtwApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer? _database;
    private readonly string _attachmentPath = Path.Combine(
        Path.GetTempPath(),
        "ptw-online-tests",
        Guid.NewGuid().ToString("N"));
    private readonly string _generatedDocumentPath = Path.Combine(
        Path.GetTempPath(),
        "ptw-online-tests-documents",
        Guid.NewGuid().ToString("N"));
    private string _connectionString;

    public PtwApiFactory()
    {
        _connectionString = Environment.GetEnvironmentVariable("PTW_TEST_CONNECTION_STRING") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            _database = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-latest").Build();
        }
    }

    public string ConnectionString => _connectionString;

    public FakeDirectoryAuthenticator DirectoryAuthenticator { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PtwDb"] = _connectionString,
                ["Attachments:Enabled"] = "true",
                ["Attachments:MaxFileBytes"] = "1048576",
                ["Attachments:MaxFilesPerPermit"] = "20",
                ["Attachments:RequireMalwareScan"] = "false",
                ["Attachments:StoragePath"] = _attachmentPath,
                ["IssuancePolicy:Approved"] = "true",
                ["IssuancePolicy:RuleVersion"] = "integration-rules-v1",
                ["IssuancePolicy:PrintTemplateVersion"] = "integration-print-v1",
                ["IssuancePolicy:CampaignAssetVersion"] = "integration-campaign-v1",
                ["LocationRelease:AreaOwnerDepartments:ORF"] = "Departemen Distribusi Gas dan Pengelolaan ORF",
                ["LocationRelease:AreaOwnerDepartments:SITE_OFFICE"] = "Departemen General Affair",
                ["LocationRelease:AreaOwnerDepartments:WATER_BASED"] = "Departemen Transport & Operasi FSRU",
                ["GeneratedDocuments:Enabled"] = "true",
                ["GeneratedDocuments:StoragePath"] = _generatedDocumentPath,
                ["GeneratedDocuments:MaxRenderAttempts"] = "3"
            }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<PtwDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<PtwDbContext>>();
            services.RemoveAll<IssuancePolicySettings>();
            services.RemoveAll<LocationReleaseSettings>();
            services.RemoveAll<UserAuthorizationRoleProfileSettings>();
            services.RemoveAll<IDirectoryAuthenticator>();
            services.AddSingleton<IDirectoryAuthenticator>(DirectoryAuthenticator);
            services.AddSingleton(new IssuancePolicySettings
            {
                Approved = true,
                RuleVersion = "integration-rules-v1",
                PrintTemplateVersion = "integration-print-v1",
                CampaignAssetVersion = "integration-campaign-v1"
            });
            services.AddSingleton(new LocationReleaseSettings
            {
                AreaOwnerDepartments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ORF"] = "Departemen Distribusi Gas dan Pengelolaan ORF",
                    ["SITE_OFFICE"] = "Departemen General Affair",
                    ["WATER_BASED"] = "Departemen Transport & Operasi FSRU"
                }
            });
            services.AddSingleton(new UserAuthorizationRoleProfileSettings
            {
                Roles = new Dictionary<string, UserAuthorizationRoleProfile>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Administrator"] = new()
                    {
                        Label = "Administrator",
                        ActionCodes = ["admin.manage"]
                    },
                    ["AreaOwnerManager"] = new()
                    {
                        Label = "Manager Pemilik Wilayah",
                        LocationRequired = true,
                        ActionCodes = ["permit.approve-and-issue", "permit.suspend"]
                    }
                }
            });
            services.AddDbContext<PtwDbContext>(options => options.UseSqlServer(_connectionString));
        });
    }

    public async Task InitializeAsync()
    {
        if (_database is not null)
        {
            await _database.StartAsync();
            _connectionString = _database.GetConnectionString();
        }
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PtwDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
        if (Directory.Exists(_attachmentPath))
        {
            Directory.Delete(_attachmentPath, recursive: true);
        }
        if (Directory.Exists(_generatedDocumentPath))
        {
            Directory.Delete(_generatedDocumentPath, recursive: true);
        }
        Dispose();
    }
}
