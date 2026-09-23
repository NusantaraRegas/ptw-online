using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ptw.Application;
using Ptw.Infrastructure.Persistence;
using Ptw.Infrastructure.Security;

namespace Ptw.Infrastructure;

public static class InfrastructureServices
{
    public static IServiceCollection AddPtwInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment = false)
    {
        var connectionString = configuration.GetConnectionString("PtwDb")
            ?? throw new InvalidOperationException("ConnectionStrings:PtwDb wajib dikonfigurasi.");
        services.AddDbContext<PtwDbContext>(options => options.UseSqlServer(connectionString, sql =>
            sql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null)));
        services.AddScoped<IPermitStore, PermitStore>();
        services.AddScoped<IPermitAttachmentStore, PermitAttachmentStore>();
        services.AddScoped<ILocationMasterStore, LocationMasterStore>();
        services.AddScoped<IUserAuthorizationStore, UserAuthorizationStore>();
        services.AddScoped<IUserDirectoryStore, UserDirectoryStore>();
        services.AddScoped<IDemoModeStore, DemoModeStore>();
        services.AddScoped<IAuthorizationAssignmentResolver, AuthorizationAssignmentResolver>();
        services.AddScoped<IPolicyUatStore, PolicyUatStore>();
        services.AddSingleton<IClock, SystemClock>();
        var demoModeEnabledByDefault = isDevelopment
            && (!bool.TryParse(configuration["DemoMode:EnabledByDefault"], out var configuredDemoMode)
                || configuredDemoMode);
        services.AddSingleton(new DemoModeDefaults(demoModeEnabledByDefault));
        services.AddSingleton<IPermitNumberGenerator, PermitNumberGenerator>();
        var activeDirectorySettings = ActiveDirectorySettings.FromConfiguration(configuration);
        services.AddSingleton(activeDirectorySettings);
        // Fail-safe: without a configured directory, login relies on local credentials only.
        services.AddSingleton<IDirectoryAuthenticator>(provider =>
            activeDirectorySettings.Enabled
                ? new LdapDirectoryAuthenticator(
                    activeDirectorySettings,
                    provider.GetRequiredService<ILogger<LdapDirectoryAuthenticator>>())
                : new DisabledDirectoryAuthenticator());
        var attachmentSettings = new AttachmentSettings
        {
            Enabled = bool.TryParse(configuration["Attachments:Enabled"], out var enabled) && enabled,
            MaxFileBytes = long.TryParse(configuration["Attachments:MaxFileBytes"], out var maxBytes)
                ? maxBytes
                : 0,
            MaxFilesPerPermit = int.TryParse(
                configuration["Attachments:MaxFilesPerPermit"],
                out var maxFiles)
                ? maxFiles
                : 0,
            RequireMalwareScan = !bool.TryParse(
                    configuration["Attachments:RequireMalwareScan"],
                    out var requireMalwareScan)
                || requireMalwareScan,
            StoragePath = configuration["Attachments:StoragePath"] ?? string.Empty
        };
        if (attachmentSettings.Enabled
            && (attachmentSettings.MaxFileBytes <= 0
                || attachmentSettings.MaxFilesPerPermit <= 0
                || string.IsNullOrWhiteSpace(attachmentSettings.StoragePath)))
        {
            throw new InvalidOperationException(
                "Attachments yang aktif memerlukan MaxFileBytes, MaxFilesPerPermit, dan StoragePath yang valid.");
        }

        services.AddSingleton(attachmentSettings);
        services.AddSingleton(new AttachmentPolicy(
            attachmentSettings.Enabled,
            attachmentSettings.MaxFileBytes,
            attachmentSettings.MaxFilesPerPermit,
            attachmentSettings.RequireMalwareScan));
        services.AddSingleton<IAttachmentStorage>(provider =>
            attachmentSettings.Enabled
                ? new LocalAttachmentStorage(provider.GetRequiredService<AttachmentSettings>())
                : new DisabledAttachmentStorage());
        services.AddSingleton<IMalwareScanner>(provider =>
            isDevelopment && !attachmentSettings.RequireMalwareScan
                ? new DevelopmentUploadTrustScanner(provider.GetRequiredService<IClock>())
                : new UnavailableMalwareScanner());

        var generatedDocumentSettings = new GeneratedDocumentSettings
        {
            Enabled = bool.TryParse(configuration["GeneratedDocuments:Enabled"], out var documentsEnabled)
                && documentsEnabled,
            StoragePath = configuration["GeneratedDocuments:StoragePath"] ?? string.Empty,
            MaxRenderAttempts = int.TryParse(
                configuration["GeneratedDocuments:MaxRenderAttempts"],
                out var maxRenderAttempts) && maxRenderAttempts > 0
                ? maxRenderAttempts
                : 5
        };
        if (generatedDocumentSettings.Enabled && string.IsNullOrWhiteSpace(generatedDocumentSettings.StoragePath))
        {
            throw new InvalidOperationException(
                "GeneratedDocuments yang aktif memerlukan StoragePath yang valid.");
        }

        services.AddSingleton(generatedDocumentSettings);
        services.AddScoped<IPrintPackageStore, PrintPackageStore>();
        services.AddSingleton<IGeneratedDocumentStorage>(provider =>
            generatedDocumentSettings.Enabled
                ? new LocalGeneratedDocumentStorage(provider.GetRequiredService<GeneratedDocumentSettings>())
                : new DisabledGeneratedDocumentStorage());
        // Fail-closed by default: without an approved template binding no official document is produced.
        services.AddSingleton<IPrintPackageRenderer>(_ =>
            generatedDocumentSettings.Enabled
                ? new Printing.PtwFormRenderer()
                : new UnavailablePrintPackageRenderer());
        services.AddScoped<PrintPackageRenderExecutor>();
        return services;
    }
}

internal sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

internal sealed class PermitNumberGenerator : IPermitNumberGenerator
{
    public string Generate(DateTimeOffset now) => $"PTW-{now:yyyyMMdd}-{Guid.NewGuid():N}"[..21].ToUpperInvariant();
}
