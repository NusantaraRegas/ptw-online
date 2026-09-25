using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Ptw.Application;

namespace Ptw.Infrastructure.Security;

/// <summary>
/// Refuses passwords that appear in public breach corpora. The embedded deny-list covers the most
/// reused passwords of twelve characters or more and always applies. When the online check is
/// enabled, the first five hex characters of the SHA-1 are sent (k-anonymity) and the suffix is
/// matched locally; the password itself never leaves the process. An unreachable service is a
/// refusal, never a silent pass, because the check exists for break-glass accounts.
/// </summary>
public sealed class BreachedPasswordChecker(
    BreachedPasswordSettings settings,
    HttpClient httpClient,
    ILogger<BreachedPasswordChecker> logger) : IBreachedPasswordChecker
{
    private static readonly Action<ILogger, string, Exception?> OnlineCheckUnavailable =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1120, "BreachedPasswordCheckUnavailable"),
            "Pemeriksaan kebocoran password tidak tersedia ({Reason}); password ditolak (fail-closed).");

    public async Task<bool> IsBreachedAsync(string password, CancellationToken cancellationToken)
    {
        if (CommonPasswordDenyList.Contains(password))
        {
            return true;
        }

        if (!settings.OnlineEnabled)
        {
            return false;
        }

        // SHA-1 is the protocol identifier of the Pwned Passwords range API, not a security control
        // here: only the first five hex characters ever leave the process.
#pragma warning disable CA5350
        var sha1 = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
#pragma warning restore CA5350
        var prefix = sha1[..5];
        var suffix = sha1[5..];
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(settings.BaseUrl, prefix));
            // Padding hides the real number of matches from a network observer.
            request.Headers.TryAddWithoutValidation("Add-Padding", "true");
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                OnlineCheckUnavailable(logger, $"HTTP {(int)response.StatusCode}", null);
                throw new BreachedPasswordCheckUnavailableException();
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var separator = line.IndexOf(':', StringComparison.Ordinal);
                var candidate = separator < 0 ? line : line[..separator];
                if (candidate.Equals(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    // Padded entries carry a count of 0 and are not real matches.
                    var count = separator < 0 ? "1" : line[(separator + 1)..];
                    return !count.Trim().Equals("0", StringComparison.Ordinal);
                }
            }

            return false;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            OnlineCheckUnavailable(logger, exception.GetType().Name, exception);
            throw new BreachedPasswordCheckUnavailableException();
        }
    }
}
