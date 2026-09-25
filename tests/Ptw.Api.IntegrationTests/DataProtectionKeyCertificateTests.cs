using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Ptw.Api.Security;

namespace Ptw.Api.IntegrationTests;

public sealed class DataProtectionKeyCertificateTests
{
    [Fact]
    public void MissingCertificateIsOptionalInDevelopmentAndFatalElsewhere()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.Null(DataProtectionKeyCertificate.Load(configuration, isDevelopment: true));
        var exception = Assert.Throws<InvalidOperationException>(() =>
            DataProtectionKeyCertificate.Load(configuration, isDevelopment: false));
        Assert.Contains("DataProtectionCertificatePath", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PfxWithPrivateKeyIsLoaded()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ptw-dp-{Guid.NewGuid():N}.pfx");
        try
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=NrPtwOnline-Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddYears(1));
            File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, "test-password"));
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DataProtectionKeyCertificate.PathKey] = path,
                [DataProtectionKeyCertificate.PasswordKey] = "test-password"
            }).Build();

            using var loaded = DataProtectionKeyCertificate.Load(configuration, isDevelopment: false);

            Assert.NotNull(loaded);
            Assert.True(loaded.HasPrivateKey);
            Assert.Equal(certificate.Thumbprint, loaded.Thumbprint);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MissingFileIsFatal()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [DataProtectionKeyCertificate.PathKey] = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.pfx")
        }).Build();

        Assert.Throws<InvalidOperationException>(() => DataProtectionKeyCertificate.Load(configuration, isDevelopment: true));
    }
}
