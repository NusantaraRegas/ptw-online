using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Ptw.Application;
using Ptw.Domain;

namespace Ptw.Api;

internal sealed class ApiExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    private static readonly Action<ILogger, string, Exception?> LogUnexpected =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(1000, "UnhandledApiException"), "Unhandled API exception. TraceId={TraceId}");
    private static readonly Action<ILogger, string, string, Exception?> LogRejected =
        LoggerMessage.Define<string, string>(LogLevel.Warning, new EventId(1001, "ApiRequestRejected"), "API request rejected. Code={Code} TraceId={TraceId}");
    private static readonly Action<ILogger, string, string, DateTimeOffset?, string, Exception?> LogLockout =
        LoggerMessage.Define<string, string, DateTimeOffset?, string>(
            LogLevel.Warning,
            new EventId(1002, "LocalAccountLockedOut"),
            "Akun lokal terkunci atau masih terkunci. UserName={UserName} SourceAddress={SourceAddress} LockedUntil={LockedUntil} TraceId={TraceId}");

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code, title) = exception switch
        {
            ResourceNotFoundException => (StatusCodes.Status404NotFound, "resource.not_found", "Data tidak ditemukan"),
            // Kestrel raises this for a body above MaxRequestBodySize or a malformed request; it is
            // the client's fault and must not be reported as a server error.
            BadHttpRequestException badRequest => (
                badRequest.StatusCode,
                badRequest.StatusCode == StatusCodes.Status413PayloadTooLarge ? "request.body_too_large" : "request.malformed",
                "Permintaan ditolak"),
            AuthenticationFailedException => (StatusCodes.Status401Unauthorized, "authentication.invalid_credentials", "Login gagal"),
            // Same status and code as any other failed login so the response never reveals lockout state.
            AccountLockedOutException => (StatusCodes.Status401Unauthorized, "authentication.invalid_credentials", "Login gagal"),
            BreachedPasswordCheckUnavailableException =>
                (StatusCodes.Status503ServiceUnavailable, "user.password_breach_check_unavailable", "Pemeriksaan password tidak tersedia"),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "authorization.denied", "Akses ditolak"),
            ConcurrencyConflictException => (StatusCodes.Status409Conflict, "concurrency.conflict", "Konflik versi"),
            PolicyAuthorizationDeniedException denied =>
                (StatusCodes.Status403Forbidden, denied.Code, "Otorisasi policy menolak aksi"),
            PolicyActivationException =>
                (StatusCodes.Status503ServiceUnavailable, "policy.activation_not_ready", "Policy belum siap"),
            InvalidRequestException { Code: "idempotency.payload_mismatch" } request =>
                (StatusCodes.Status409Conflict, request.Code, "Konflik idempotency"),
            DomainRuleViolationException domain => (StatusCodes.Status409Conflict, domain.Code, "Aturan domain menolak aksi"),
            InvalidRequestException request => (StatusCodes.Status422UnprocessableEntity, request.Code, "Permintaan tidak valid"),
            _ => (StatusCodes.Status500InternalServerError, "server.unexpected", "Terjadi kesalahan pada server")
        };

        // Activation failures are expected fail-closed configuration outcomes. Their messages are
        // deliberately operator-safe and tell the user why an otherwise valid command is blocked.
        var expectedConfigurationFailure = exception is PolicyActivationException or BreachedPasswordCheckUnavailableException;
        if (status >= 500 && !expectedConfigurationFailure)
        {
            LogUnexpected(logger, httpContext.TraceIdentifier, exception);
        }
        else if (exception is AccountLockedOutException lockout)
        {
            // Lockouts are the signal of an attack on a named account; the operator log carries the
            // source address so it can be correlated with the nginx log and the login journal.
            LogLockout(logger, lockout.UserName, lockout.SourceAddress, lockout.LockedUntil, httpContext.TraceIdentifier, null);
        }
        else
        {
            LogRejected(logger, code, httpContext.TraceIdentifier, null);
        }

        httpContext.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = status >= 500 && !expectedConfigurationFailure
                    ? "Gunakan traceId untuk menghubungi support."
                    : exception.Message,
                Extensions =
                {
                    ["code"] = code,
                    ["traceId"] = httpContext.TraceIdentifier
                }
            },
            Exception = exception
        });
    }
}
