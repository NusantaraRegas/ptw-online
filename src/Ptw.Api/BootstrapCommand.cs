using Ptw.Application;

namespace Ptw.Api;

/// <summary>
/// Host entry point for <c>dotnet Ptw.Api.dll --reset-password &lt;subjectId&gt;</c>. Returns a
/// process exit code and prints an operator-safe outcome; the password is read from standard input
/// and is never echoed or logged.
/// </summary>
public static class BootstrapCommand
{
    private static readonly Action<ILogger, string, string, Exception?> PasswordReset =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(1110, "AdministratorPasswordBootstrapped"),
            "Password Administrator di-bootstrap di luar API. SubjectId={SubjectId} CorrelationId={CorrelationId}");

    private static readonly Action<ILogger, string, string, Exception?> PasswordResetRejected =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(1111, "AdministratorPasswordBootstrapRejected"),
            "Bootstrap password Administrator ditolak. SubjectId={SubjectId} Reason={Reason}");

    public static async Task<int> ResetAdministratorPasswordAsync(
        IServiceProvider services,
        ILogger logger,
        string subjectId,
        TextReader passwordInput)
    {
        if (string.IsNullOrWhiteSpace(subjectId))
        {
            Console.Error.WriteLine("Pemakaian: --reset-password <subjectId>  (password baru dibaca dari stdin, satu baris)");
            return 2;
        }

        var password = (await passwordInput.ReadLineAsync())?.TrimEnd('\r') ?? string.Empty;
        if (password.Length == 0)
        {
            Console.Error.WriteLine("Password baru harus diberikan lewat stdin, misalnya: printf '%s\\n' \"$PASSWORD\" | ... --reset-password <subjectId>");
            return 2;
        }

        await using var scope = services.CreateAsyncScope();
        var bootstrap = scope.ServiceProvider.GetRequiredService<AdministratorPasswordBootstrap>();
        try
        {
            var result = await bootstrap.ResetAsync(subjectId, password, CancellationToken.None);
            PasswordReset(logger, result.SubjectId, result.CorrelationId, null);
            Console.WriteLine($"Password akun Administrator '{result.UserName}' ({result.SubjectId}) diperbarui. Sesi lama berakhir. Korelasi audit: {result.CorrelationId}");
            return 0;
        }
        catch (Exception exception) when (exception is InvalidRequestException or ResourceNotFoundException or BreachedPasswordCheckUnavailableException)
        {
            var reason = exception is InvalidRequestException invalid ? invalid.Code : exception.GetType().Name;
            PasswordResetRejected(logger, subjectId.Trim(), reason, null);
            Console.Error.WriteLine($"Ditolak: {exception.Message}");
            return 1;
        }
    }
}
