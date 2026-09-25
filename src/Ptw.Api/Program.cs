using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using Ptw.Api;
using Ptw.Api.Security;
using Ptw.Application;
using Ptw.Infrastructure;
using Ptw.Infrastructure.Persistence;
using Ptw.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

var dataProtectionPath = builder.Configuration["Authentication:DataProtectionPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionPath))
{
    Directory.CreateDirectory(dataProtectionPath);
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath))
        .SetApplicationName("NrPtwOnline");
}

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IActorContext, HttpActorContext>();
builder.Services.AddScoped<PermitService>();
builder.Services.AddScoped<PermitAttachmentService>();
builder.Services.AddScoped<PrintPackageService>();
builder.Services.AddScoped<LocationMasterService>();
builder.Services.AddScoped<LocationLookupService>();
builder.Services.AddScoped<UserAuthorizationService>();
builder.Services.AddScoped<UserDirectoryService>();
builder.Services.AddScoped<UserAuthenticationService>();
builder.Services.AddScoped<DemoModeService>();
builder.Services.AddScoped<OperationalPolicyService>();
builder.Services.AddScoped<PolicySimulationService>();
builder.Services.AddScoped<PolicyUatService>();
builder.Services.AddScoped<IOperationalPolicyGate, OperationalPolicyGate>();
builder.Services.AddSingleton(
    builder.Configuration.GetSection("OperationalPolicy").Get<OperationalPolicySettings>()
    ?? new OperationalPolicySettings());
builder.Services.AddSingleton(
    builder.Configuration.GetSection("LocationRelease").Get<LocationReleaseSettings>()
    ?? new LocationReleaseSettings());
builder.Services.AddSingleton(
    builder.Configuration.GetSection("IssuancePolicy").Get<IssuancePolicySettings>()
    ?? new IssuancePolicySettings());
builder.Services.AddSingleton(
    builder.Configuration.GetSection("UserAuthorizationRoleProfiles")
        .Get<UserAuthorizationRoleProfileSettings>()
    ?? new UserAuthorizationRoleProfileSettings());
builder.Services.AddSingleton(
    builder.Configuration.GetSection("UserAuthorizationApproval")
        .Get<UserAuthorizationApprovalSettings>()
    ?? new UserAuthorizationApprovalSettings());
builder.Services.AddPtwInfrastructure(builder.Configuration, builder.Environment.IsDevelopment());
// Resolved from IConfiguration at first use rather than from builder.Configuration here, so sources
// appended after the builder phase (the integration-test host does this) still apply. Login settings
// are validated right after Build() below so a bad configuration fails startup, not the first login.
builder.Services.AddSingleton(provider => LoginSettings.FromConfiguration(
    provider.GetRequiredService<IConfiguration>(),
    provider.GetRequiredService<IHostEnvironment>().IsDevelopment(),
    provider.GetRequiredService<PortalAuthenticationSettings>()));
builder.Services.AddSingleton(provider =>
    provider.GetRequiredService<IConfiguration>().GetSection(RateLimitSettings.SectionName).Get<RateLimitSettings>()
    ?? new RateLimitSettings());
// X-Forwarded-* headers are honoured only from the reverse proxy networks listed in configuration.
// Without that list the API keeps the direct connection address, so nothing outside the proxy can
// spoof a client address into the rate limiter or the cookie scheme.
var knownProxyNetworks = builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [];
var knownProxies = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
var forwardedHeadersConfigured = knownProxyNetworks.Length > 0 || knownProxies.Length > 0;
if (forwardedHeadersConfigured)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        foreach (var network in knownProxyNetworks)
        {
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        }
        foreach (var proxy in knownProxies)
        {
            options.KnownProxies.Add(IPAddress.Parse(proxy));
        }
    });
}
var attachmentMaxFileBytes = builder.Configuration.GetValue<long>("Attachments:MaxFileBytes");
if (attachmentMaxFileBytes > 0)
{
    builder.Services.Configure<FormOptions>(options =>
    {
        options.MemoryBufferThreshold = 64 * 1024;
        options.MultipartBodyLengthLimit = attachmentMaxFileBytes + 1024 * 1024;
    });
}
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = "PtwIdentity";
        options.DefaultAuthenticateScheme = "PtwIdentity";
        options.DefaultChallengeScheme = "PtwIdentity";
    })
    .AddPolicyScheme("PtwIdentity", "PTW identity", options =>
    {
        options.ForwardDefaultSelector = context =>
            context.Request.Cookies.ContainsKey("ptw.development.session")
                ? CookieAuthenticationDefaults.AuthenticationScheme
                : DevelopmentAuthenticationHandler.SchemeName;
    })
    .AddCookie(options =>
    {
        options.Cookie.Name = "ptw.development.session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        // Development serves http://localhost; everywhere else the cookie must never travel in clear.
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
        options.Events.OnValidatePrincipal = async context =>
        {
            var subjectId = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var store = context.HttpContext.RequestServices.GetRequiredService<IUserDirectoryStore>();
            var resolved = string.IsNullOrWhiteSpace(subjectId)
                ? null
                : await store.ResolveIdentityAsync(subjectId, DateTimeOffset.UtcNow, context.HttpContext.RequestAborted);
            if (resolved is null || !resolved.IsActive)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }

            var identity = (System.Security.Claims.ClaimsIdentity)context.Principal!.Identity!;
            foreach (var claim in identity.FindAll(System.Security.Claims.ClaimTypes.Role).ToArray())
            {
                identity.RemoveClaim(claim);
            }
            foreach (var claim in identity.FindAll("location_scope").ToArray())
            {
                identity.RemoveClaim(claim);
            }
            var currentName = identity.FindFirst(System.Security.Claims.ClaimTypes.Name);
            if (currentName is not null)
            {
                identity.RemoveClaim(currentName);
            }
            identity.AddClaim(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, resolved.DisplayName));
            identity.AddClaims(resolved.Roles.Select(role => new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, role)));
            identity.AddClaims(resolved.LocationScopes.Select(scope => new System.Security.Claims.Claim("location_scope", scope)));
            identity.AddClaims(resolved.CompetencyCodes.Select(code => new System.Security.Claims.Claim("competency", code)));
        };
    })
    .AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>(
        DevelopmentAuthenticationHandler.SchemeName,
        _ => { });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    // The proxy (nginx limit_req) is the primary limiter; this is an in-process backstop. Signed-in
    // callers are keyed by subject id so two accounts with the same display name never share a
    // bucket, and anonymous callers by the client address the forwarded-headers policy resolved.
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var settings = context.RequestServices.GetRequiredService<RateLimitSettings>();
        return RateLimitPartition.GetFixedWindowLimiter(
            RateLimitSettings.ClientKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = settings.GlobalPermitLimit,
                Window = TimeSpan.FromSeconds(settings.WindowSeconds),
                QueueLimit = 0
            });
    });
    // Login forwards the credential to the Portal API (Active Directory). A tighter per-address
    // window slows password spraying and lockout attacks against named staff accounts.
    options.AddPolicy(RateLimitSettings.LoginPolicy, context =>
    {
        var settings = context.RequestServices.GetRequiredService<RateLimitSettings>();
        return RateLimitPartition.GetFixedWindowLimiter(
            RateLimitSettings.AddressKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = settings.LoginPermitLimit,
                Window = TimeSpan.FromSeconds(settings.WindowSeconds),
                QueueLimit = 0
            });
    });
});
builder.Services.AddHealthChecks().AddCheck<PtwDatabaseHealthCheck>("ptw-database", tags: ["ready"]);

var app = builder.Build();

if (args.Contains("--migrate", StringComparer.OrdinalIgnoreCase))
{
    await using var migrationScope = app.Services.CreateAsyncScope();
    var migrationDb = migrationScope.ServiceProvider.GetRequiredService<PtwDbContext>();
    await migrationDb.Database.MigrateAsync();
    return;
}

// Fail fast on an inconsistent login configuration (OPN-007) instead of on the first login attempt.
_ = app.Services.GetRequiredService<LoginSettings>();
StartupLog.WarnOnAcceptedRisks(app.Logger, app.Services.GetRequiredService<PortalAuthenticationSettings>());

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (forwardedHeadersConfigured)
{
    app.UseForwardedHeaders();
}
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    // API responses carry permit and personal data; browsers and intermediaries must not cache
    // them. Endpoints that already chose a cache policy keep it.
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.OnStarting(() =>
        {
            if (StringValues.IsNullOrEmpty(context.Response.Headers.CacheControl))
            {
                context.Response.Headers.CacheControl = "no-store";
            }
            return Task.CompletedTask;
        });
    }
    await next();
});
app.Use(async (context, next) =>
{
    const string header = "X-Correlation-ID";
    var correlationId = context.Request.Headers[header].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 100)
    {
        correlationId = context.TraceIdentifier;
    }
    context.Items[header] = correlationId;
    context.Response.Headers[header] = correlationId;
    await next();
});
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers().RequireAuthorization();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

await app.RunAsync();

public partial class Program;

internal sealed class PtwDatabaseHealthCheck(IServiceScopeFactory scopeFactory) : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    public async Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PtwDbContext>();
        return await db.Database.CanConnectAsync(cancellationToken)
            ? Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy()
            : Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("SQL Server tidak dapat dijangkau.");
    }
}
