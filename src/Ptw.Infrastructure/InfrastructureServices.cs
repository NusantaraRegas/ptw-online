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
        services.AddScoped<ILocationMasterStore, LocationMasterStore>();
        services.AddScoped<IUserAuthorizationStore, UserAuthorizationStore>();
        services.AddScoped<IAuthorizationAssignmentResolver, AuthorizationAssignmentResolver>();
        services.AddScoped<IPolicyUatStore, PolicyUatStore>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPermitNumberGenerator, PermitNumberGenerator>();
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
