using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PtwDb"] = _connectionString
            }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<PtwDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<PtwDbContext>>();
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
        Dispose();
    }
}
