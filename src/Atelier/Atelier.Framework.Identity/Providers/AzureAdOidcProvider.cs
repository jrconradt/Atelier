using Atelier.Framework.Attributes;
using Atelier.Framework.Identity.Models;
using Atelier.Framework.Identity.Services;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using System.Net.Http.Json;
using System.Text.Json;

namespace Atelier.Framework.Identity.Providers;

[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class AzureAdOidcProvider : GenericOidcProvider, IAtelier
{
    public override async Task<Outcome<OidcUserInfo>> GetUserInfoAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {

        try
        {
            var userInfoEndpoint = await GetUserInfoEndpointAsync(cancellationToken).ConfigureAwait(false);
            if (!userInfoEndpoint.IsSuccess)
            {
                return Outcome<OidcUserInfo>.Failure();
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, userInfoEndpoint.Data);
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Observe(
                    LogLevel.Warning,
                    values: [("Event", "UserInfoRequestFailed"), ("Reason", "Azure AD user info request returned non-success status"), ("StatusCode", response.StatusCode), ("Provider", ProviderName)]);
                return Outcome<OidcUserInfo>.Failure();
            }

            var userInfoResponse = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                cancellationToken).ConfigureAwait(false);

            if (userInfoResponse == null)
            {
                Observe(
                    LogLevel.Warning,
                    values: [("Event", "UserInfoRequestFailed"), ("Reason", "Failed to deserialize Azure AD user info response"), ("Provider", ProviderName)]);
                return Outcome<OidcUserInfo>.Failure();
            }

            var userInfo = MapAzureAdUserInfo(userInfoResponse);
            return Outcome<OidcUserInfo>.Success(userInfo);
        }
        catch (Exception ex)
        {
            Observe(
                LogLevel.Error,
                ex,
                values: [("Event", "UserInfoRequestFailed"), ("Reason", "Azure AD user info retrieval failed"), ("Provider", ProviderName)]);

            return Outcome<OidcUserInfo>.Failure();
        }
    }

    public override async Task<Outcome<string>> GetAuthorizationUrlAsync(
        string? state = null,
        string? nonce = null,
        string? codeChallenge = null,
        string? codeChallengeMethod = null,
        CancellationToken cancellationToken = default)
    {

        try
        {
            var authEndpoint = await GetAuthorizationEndpointAsync(cancellationToken).ConfigureAwait(false);
            if (!authEndpoint.IsSuccess)
            {
                return Outcome<string>.Failure();
            }

            var parameters = new Dictionary<string, string>
            {
                ["response_type"] = string.Join(" ", _config.ResponseTypes),
                ["client_id"] = _config.ClientId,
                ["redirect_uri"] = _config.RedirectUri ?? string.Empty,
                ["scope"] = string.Join(" ", _config.Scopes),
                ["response_mode"] = "query"
            };

            if (!string.IsNullOrEmpty(state))
            {
                parameters["state"] = state;
            }
            if (!string.IsNullOrEmpty(nonce))
            {
                parameters["nonce"] = nonce;
            }
            if (!string.IsNullOrEmpty(codeChallenge))
            {
                parameters["code_challenge"] = codeChallenge;
            }
            if (!string.IsNullOrEmpty(codeChallengeMethod))
            {
                parameters["code_challenge_method"] = codeChallengeMethod;
            }

            var queryString = string.Join("&", parameters.Select(kvp =>
                $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
            var authUrl = $"{authEndpoint.Data}?{queryString}";

            return Outcome<string>.Success(authUrl);
        }
        catch (Exception ex)
        {
            Observe(
                LogLevel.Error,
                ex,
                values: [("Event", "AuthorizationUrlFailed"), ("Reason", "Azure AD authorization URL generation failed"), ("Provider", ProviderName)]);

            return Outcome<string>.Failure();
        }
    }

    private OidcUserInfo MapAzureAdUserInfo(Dictionary<string, object> claims)
    {
        ArgumentNullException.ThrowIfNull(claims);
        return new OidcUserInfo
        {
            Subject = OidcClaimHelpers.GetClaimValue(claims, "oid") ?? OidcClaimHelpers.GetClaimValue(claims, "sub") ?? string.Empty,
            Name = OidcClaimHelpers.GetClaimValue(claims, "name"),
            Email = OidcClaimHelpers.GetClaimValue(claims, "email") ?? OidcClaimHelpers.GetClaimValue(claims, "upn"),
            Username = OidcClaimHelpers.GetClaimValue(claims, "preferred_username") ?? OidcClaimHelpers.GetClaimValue(claims, "upn"),
            Picture = OidcClaimHelpers.GetClaimValue(claims, "picture"),
            Locale = OidcClaimHelpers.GetClaimValue(claims, "locale"),
            ZoneInfo = OidcClaimHelpers.GetClaimValue(claims, "zoneinfo"),
            EmailVerified = OidcClaimHelpers.GetBooleanClaimValue(claims, "email_verified"),
            PhoneNumberVerified = OidcClaimHelpers.GetBooleanClaimValue(claims, "phone_number_verified"),
            PhoneNumber = OidcClaimHelpers.GetClaimValue(claims, "phone_number"),
            Address = OidcClaimHelpers.GetClaimValue(claims, "address"),
            Roles = OidcClaimHelpers.GetArrayClaimValues(claims, "roles"),
            Scopes = OidcClaimHelpers.GetArrayClaimValues(claims, "scp"),
            TenantId = OidcClaimHelpers.GetClaimValue(claims, "tid"),
            SessionId = OidcClaimHelpers.GetClaimValue(claims, "sid"),
            AdditionalClaims = OidcClaimHelpers.GetAdditionalClaims(
                claims,
                new[]
                {
                    "oid", "sub", "name", "email", "upn", "preferred_username", "picture",
                    "locale", "zoneinfo", "email_verified", "phone_number_verified",
                    "phone_number", "address", "roles", "scp", "tid", "sid"
                })
        };
    }
}
