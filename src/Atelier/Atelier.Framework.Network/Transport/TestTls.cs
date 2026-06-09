using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Atelier.Framework.Network.Transport;

internal static class TestTls
{
    private const string CERT_PASSWORD = "atelier-test-tls";

    public static TransportTlsOptions CreateSelfSignedOptions()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=localhost",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                true));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") },
                false));

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddIpAddress(System.Net.IPAddress.Loopback);
        sanBuilder.AddIpAddress(System.Net.IPAddress.IPv6Loopback);
        request.CertificateExtensions.Add(sanBuilder.Build());

        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1));

        var pfxBytes = certificate.Export(X509ContentType.Pfx, CERT_PASSWORD);
        var pfxPath = Path.Combine(Path.GetTempPath(), $"atelier-test-tls-{Guid.NewGuid():N}.pfx");
        File.WriteAllBytes(pfxPath, pfxBytes);

        return new TransportTlsOptions
        {
            CertPath = pfxPath,
            CertPassword = CERT_PASSWORD,
            AllowAnonymous = true,
        };
    }

    public static HttpClient CreateLoopbackTrustingHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true,
            },
        };

        return new HttpClient(handler);
    }
}
