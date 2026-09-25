using System.Text;

namespace Ptw.Infrastructure.Security;

/// <summary>
/// Offline deny-list for local passwords. Composition rules already reject short passwords, so the
/// list targets what people type to satisfy them: a well-known root word (dictionary favourites,
/// this organisation's names) padded with digits, symbols, or leetspeak. The candidate is reduced
/// to its letters with leetspeak undone before comparison, so "P@ssw0rd2026!" and "Pertamina#1234"
/// are both refused.
/// </summary>
internal static class CommonPasswordDenyList
{
    private static readonly HashSet<string> Roots = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "passwords", "passwort", "katasandi", "sandi", "rahasia",
        "welcome", "selamatdatang", "letmein", "changeme", "gantipassword", "default", "temporary", "sementara",
        "admin", "administrator", "adminadmin", "root", "superuser", "sysadmin", "operator", "manager", "support",
        "qwerty", "qwertyuiop", "asdfghjkl", "zxcvbnm", "abcdefgh", "abcdefghijkl", "abcabcabc",
        "iloveyou", "sunshine", "princess", "football", "baseball", "dragon", "monkey", "master", "shadow", "michael",
        "trustno", "whatever", "computer", "internet", "cheese", "summer", "winter", "spring", "autumn",
        "january", "february", "march", "april", "june", "july", "august", "september", "october", "november", "december",
        "januari", "februari", "maret", "mei", "juni", "juli", "agustus", "oktober", "desember",
        "indonesia", "jakarta", "bandung", "surabaya", "merdeka", "pancasila", "garuda", "nusantara",
        "nusantararegas", "regas", "pertamina", "pertaminagas", "pgn", "ptw", "ptwonline", "permit", "permittowork",
        "fsru", "orf", "lng", "muarakarang", "tanjungpriok", "hse", "safety", "safetyfirst"
    };

    public static bool Contains(string password)
    {
        // Padding is what people add to satisfy length and composition rules, so it is trimmed from
        // both ends before the core is compared, with and without leetspeak undone.
        var core = password.AsSpan().Trim(" \t0123456789!@#$%^&*()-_=+[]{};:'\",.<>/?\\|`~");
        if (core.IsEmpty)
        {
            return false;
        }

        return Roots.Contains(Reduce(core, undoLeetspeak: false)) || Roots.Contains(Reduce(core, undoLeetspeak: true));
    }

    private static string Reduce(ReadOnlySpan<char> core, bool undoLeetspeak)
    {
        var builder = new StringBuilder(core.Length);
        foreach (var character in core)
        {
            if (!undoLeetspeak)
            {
                if (char.IsLetter(character))
                {
                    builder.Append(char.ToLowerInvariant(character));
                }

                continue;
            }

            var mapped = character switch
            {
                '0' => 'o',
                '1' or '!' or '|' => 'i',
                '3' => 'e',
                '4' or '@' => 'a',
                '5' or '$' => 's',
                '7' => 't',
                '8' => 'b',
                _ => character
            };
            if (char.IsLetter(mapped))
            {
                builder.Append(char.ToLowerInvariant(mapped));
            }
        }

        return builder.ToString();
    }
}
