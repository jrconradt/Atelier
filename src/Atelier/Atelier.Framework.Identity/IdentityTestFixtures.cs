using System.Net;
using System.Net.Http.Json;
using Atelier.Framework.Identity.Configuration;
using Atelier.Framework.Identity.Services;
using Atelier.Framework.Resilience;
using Atelier.Framework.Testing;
using Microsoft.Extensions.Configuration;

namespace Atelier.Framework.Identity;

[TestFixtureRegistry]
internal static class IdentityTestFixtures
{
    private const string AUTHORITY = "https://atelier-happy.example.com";

    [Fixture(typeof(TimeProvider))]
    internal static TimeProvider Time()
    {
        return TimeProvider.System;
    }

    [Fixture(typeof(GenericOidcProvider))]
    internal static GenericOidcProvider Provider()
    {
        var httpClient = new HttpClient(new DiscoveryStubHandler());
        var resilience = new ResiliencePipelineFactory(new ConfigurationBuilder().Build(), null);
        var provider = new GenericOidcProvider(
            httpClient,
            new OidcClaimsMapper(new OidcConfiguration()),
            resilience,
            null);

        return provider.Configure(
            "atelier-happy",
            new OidcProviderConfiguration
            {
                Authority = AUTHORITY,
                ClientId = "atelier-happy-client",
                ClientSecret = "atelier-happy-secret",
                RedirectUri = $"{AUTHORITY}/callback"
            });
    }

    private sealed class DiscoveryStubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var document = new Dictionary<string, object>
            {
                ["issuer"] = AUTHORITY,
                ["authorization_endpoint"] = $"{AUTHORITY}/authorize",
                ["token_endpoint"] = $"{AUTHORITY}/token",
                ["userinfo_endpoint"] = $"{AUTHORITY}/userinfo",
                ["jwks_uri"] = $"{AUTHORITY}/jwks",
                ["end_session_endpoint"] = $"{AUTHORITY}/logout",
                ["revocation_endpoint"] = $"{AUTHORITY}/revoke"
            };

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(document)
            };

            return Task.FromResult(response);
        }
    }
}
