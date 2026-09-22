using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Ptw.Api;
using Ptw.Api.Security;
using Ptw.Application;
using Ptw.Infrastructure;
using Ptw.Infrastructure.Persistence;

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
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
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
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();
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
