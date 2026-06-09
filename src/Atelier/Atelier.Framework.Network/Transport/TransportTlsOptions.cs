using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Atelier.Framework.Network.Transport
{
    public sealed class TransportTlsOptions
    {
        public string? CertPath { get; set; }
        public string? CertPassword { get; set; }
        public byte[]? CertPasswordBytes { get; set; }
        public bool RequireClientCertificate { get; set; }
        public bool RequiresMutualTls { get; set; }
        public bool AllowAnonymous { get; set; }
        public bool CheckCertificateRevocation { get; set; } = true;
        public SslProtocols EnabledSslProtocols { get; set; } = SslProtocols.Tls12 | SslProtocols.Tls13;
        public ClientCertificateValidation? ClientCertificateValidation { get; set; }

        public bool HasCertificate => !string.IsNullOrWhiteSpace(CertPath);

        public bool RequiresClientCertificate => RequireClientCertificate || RequiresMutualTls;

        public void Validate()
        {
            var requiresClientCertificate = RequiresClientCertificate;

            if (requiresClientCertificate
                && !HasCertificate)
            {
                throw new InvalidOperationException("Mutual TLS or client-certificate validation requires a server certificate; set CertPath");
            }

            if (!HasCertificate
                && (!string.IsNullOrEmpty(CertPassword) || (CertPasswordBytes != null && CertPasswordBytes.Length > 0)))
            {
                throw new InvalidOperationException("A certificate password was supplied without a certificate path");
            }

            if (requiresClientCertificate
                && AllowAnonymous)
            {
                throw new InvalidOperationException("AllowAnonymous cannot be combined with client-certificate or mutual-TLS requirements");
            }

            if (EnabledSslProtocols == SslProtocols.None)
            {
                throw new InvalidOperationException("At least one TLS protocol must be enabled; the minimum floor is Tls12");
            }

            var belowFloor = EnabledSslProtocols
                & ~(SslProtocols.Tls12 | SslProtocols.Tls13);

            if (belowFloor != SslProtocols.None)
            {
                throw new InvalidOperationException("Transport TLS requires a floor of Tls12; Ssl3/Tls/Tls11 are not permitted");
            }
        }

        public X509Certificate2 LoadCertificate()
        {
            if (string.IsNullOrWhiteSpace(CertPath))
            {
                throw new InvalidOperationException("No certificate path configured for TLS transport");
            }

            if (CertPasswordBytes != null
                && CertPasswordBytes.Length > 0)
            {
                var password = System.Text.Encoding.UTF8.GetString(CertPasswordBytes);
                try
                {
                    return X509CertificateLoader.LoadPkcs12FromFile(CertPath, password);
                }
                finally
                {
                    Array.Clear(CertPasswordBytes, 0, CertPasswordBytes.Length);
                }
            }

            if (string.IsNullOrEmpty(CertPassword))
            {
                return X509CertificateLoader.LoadCertificateFromFile(CertPath);
            }

            return X509CertificateLoader.LoadPkcs12FromFile(CertPath, CertPassword);
        }
    }
}
