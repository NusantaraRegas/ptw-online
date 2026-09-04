using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ptw.Application;
using Ptw.Infrastructure.Persistence;

namespace Ptw.Infrastructure;

public static class InfrastructureServices
{
    public static IServiceCollection AddPtwInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("PtwDb")
            ?? throw new InvalidOperationException("ConnectionStrings:PtwDb wajib dikonfigurasi.");
        services.AddDbContext<PtwDbContext>(options => options.UseSqlServer(connectionString, sql =>
            sql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null)));
        services.AddScoped<IPermitStore, PermitStore>();
        services.AddScoped<IPermitAttachmentStore, PermitAttachmentStore>();
        services.AddScoped<ILocationMasterStore, LocationMasterStore>();
        services.AddScoped<IUserAuthorizationStore, UserAuthorizationStore>();
        services.AddScoped<IAuthorizationAssignmentResolver, AuthorizationAssignmentResolver>();
        services.AddScoped<IPolicyUatStore, PolicyUatStore>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPermitNumberGenerator, PermitNumberGenerator>();
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
