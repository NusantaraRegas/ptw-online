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

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code, title) = exception switch
        {
            ResourceNotFoundException => (StatusCodes.Status404NotFound, "resource.not_found", "Data tidak ditemukan"),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "authorization.denied", "Akses ditolak"),
            ConcurrencyConflictException => (StatusCodes.Status409Conflict, "concurrency.conflict", "Konflik versi"),
            DomainRuleViolationException domain => (StatusCodes.Status409Conflict, domain.Code, "Aturan domain menolak aksi"),
            InvalidRequestException request => (StatusCodes.Status422UnprocessableEntity, request.Code, "Permintaan tidak valid"),
            _ => (StatusCodes.Status500InternalServerError, "server.unexpected", "Terjadi kesalahan pada server")
        };

        if (status >= 500)
        {
            LogUnexpected(logger, httpContext.TraceIdentifier, exception);
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
                Detail = status >= 500 ? "Gunakan traceId untuk menghubungi support." : exception.Message,
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
