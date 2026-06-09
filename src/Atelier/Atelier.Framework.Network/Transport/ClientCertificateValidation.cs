using System.Security.Cryptography.X509Certificates;
using Atelier.Framework.Context;
using Atelier.Framework.Outcomes;

namespace Atelier.Framework.Network.Transport
{
    public sealed class ClientCertificateValidation
    {
        public string? PinnedIssuer { get; set; }
        public string? PinnedSubject { get; set; }
        public string[]? PinnedThumbprints { get; set; }
        public bool CheckRevocation { get; set; } = true;
        public X509RevocationMode RevocationMode { get; set; } = X509RevocationMode.Online;
        public X509ChainPolicy? ChainPolicy { get; set; }

        public Outcome<AuthorizationContext> Validate(X509Certificate2? certificate)
        {
            if (certificate is null)
            {
                return Outcome<AuthorizationContext>.Failure();
            }

            var descriptor = $"subject='{certificate.Subject}' issuer='{certificate.Issuer}' thumbprint='{certificate.Thumbprint}'";

            if (!string.IsNullOrEmpty(PinnedIssuer)
                && !string.Equals(certificate.Issuer, PinnedIssuer, StringComparison.OrdinalIgnoreCase))
            {
                return Outcome<AuthorizationContext>.Failure();
            }

            if (!string.IsNullOrEmpty(PinnedSubject)
                && !string.Equals(certificate.Subject, PinnedSubject, StringComparison.OrdinalIgnoreCase))
            {
                return Outcome<AuthorizationContext>.Failure();
            }

            if (PinnedThumbprints is { Length: > 0 }
                && !PinnedThumbprints.Any(t => string.Equals(t, certificate.Thumbprint, StringComparison.OrdinalIgnoreCase)))
            {
                return Outcome<AuthorizationContext>.Failure();
            }

            var chainResult = BuildChain(certificate);
            if (!chainResult.IsSuccess)
            {
                return Outcome<AuthorizationContext>.Failure();
            }

            var authorization = AuthorizationContext.Create(
                userId: certificate.Subject,
                isVerified: true);
            authorization.AddClaim("certificate_thumbprint", certificate.Thumbprint);
            authorization.AddClaim("certificate_issuer", certificate.Issuer);
            authorization.ExpiresAt = certificate.NotAfter.ToUniversalTime();

            return Outcome<AuthorizationContext>.Success(authorization);
        }

        private Outcome BuildChain(X509Certificate2 certificate)
        {
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = CheckRevocation
                ? RevocationMode
                : X509RevocationMode.NoCheck;
            chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;

            if (ChainPolicy != null)
            {
                chain.ChainPolicy.TrustMode = ChainPolicy.TrustMode;
                chain.ChainPolicy.VerificationFlags = ChainPolicy.VerificationFlags;
                chain.ChainPolicy.RevocationFlag = ChainPolicy.RevocationFlag;

                if (!CheckRevocation)
                {
                    chain.ChainPolicy.RevocationMode = ChainPolicy.RevocationMode;
                }
                else if (ChainPolicy.RevocationMode != X509RevocationMode.NoCheck)
                {
                    chain.ChainPolicy.RevocationMode = ChainPolicy.RevocationMode;
                }

                foreach (var extra in ChainPolicy.ExtraStore)
                {
                    chain.ChainPolicy.ExtraStore.Add(extra);
                }

                foreach (var trusted in ChainPolicy.CustomTrustStore)
                {
                    chain.ChainPolicy.CustomTrustStore.Add(trusted);
                }
            }

            if (chain.Build(certificate))
            {
                return Outcome.Success();
            }

            var reasons = string.Join(
                "; ",
                chain.ChainStatus.Select(s => s.StatusInformation.Trim()));

            return Outcome.Failure();
        }
    }
}
