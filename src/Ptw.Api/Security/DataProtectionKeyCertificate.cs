using System.Security.Cryptography.X509Certificates;

namespace Ptw.Api.Security;

/// <summary>
/// Loads the certificate that encrypts the Data Protection key ring at rest
/// (<c>Authentication:DataProtectionCertificatePath</c> and <c>...CertificatePassword</c>). Outside
/// Development a persisted key ring without this certificate is refused at startup: a plain-text key
/// file would let anyone with volume or backup access forge session cookies.
/// </summary>
public static class DataProtectionKeyCertificate
{
    public const string PathKey = "Authentication:DataProtectionCertificatePath";
    public const string PasswordKey = "Authentication:DataProtectionCertificatePassword";

    public static X509Certificate2? Load(IConfiguration configuration, bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var path = configuration[PathKey]?.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            if (isDevelopment)
            {
                return null;
            }

            throw new InvalidOperationException(
                "Authentication:DataProtectionPath di luar Development memerlukan Authentication:DataProtectionCertificatePath "
                + "agar key ring tersimpan terenkripsi.");
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Sertifikat Data Protection tidak ditemukan: {path}");
        }

        var certificate = X509CertificateLoader.LoadPkcs12FromFile(
            path,
            configuration[PasswordKey],
            X509KeyStorageFlags.EphemeralKeySet);
        if (!certificate.HasPrivateKey)
        {
            throw new InvalidOperationException("Sertifikat Data Protection harus menyertakan private key (PFX).");
        }

        return certificate;
    }
}
