namespace Ptw.Application;

/// <summary>
/// The single rule set for local (break-glass) passwords, shared by the Administrator user
/// directory and the one-off bootstrap command so no path can set a weaker password than another.
/// </summary>
public sealed class LocalPasswordPolicy(IBreachedPasswordChecker breachedPasswords)
{
    public async Task ValidateAsync(string password, string userName, CancellationToken cancellationToken)
    {
        if (password.Length < 12 || password.Length > 128
            || !password.Any(char.IsUpper)
            || !password.Any(char.IsLower)
            || !password.Any(char.IsDigit))
        {
            throw new InvalidRequestException(
                "user.password_weak",
                "Password harus 12-128 karakter dan memuat huruf besar, huruf kecil, serta angka.");
        }

        var normalizedUserName = userName.Trim();
        if (normalizedUserName.Length >= 4
            && password.Contains(normalizedUserName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidRequestException(
                "user.password_contains_username",
                "Password tidak boleh memuat username.");
        }

        // Local passwords exist for break-glass Administrator access, so a password that already
        // circulates in breach corpora is refused even when it satisfies the composition rule.
        if (await breachedPasswords.IsBreachedAsync(password, cancellationToken))
        {
            throw new InvalidRequestException(
                "user.password_breached",
                "Password ini ditemukan pada daftar kebocoran data publik. Gunakan password lain.");
        }
    }
}
