using Atelier.Framework.Identity.Models;
using System.Security.Claims;
using Atelier.Framework.Attributes;

namespace Atelier.Framework.Identity.Interfaces;

public interface IOidcClaimsMapper
{
    OidcUserInfo MapToUserInfo(
        Dictionary<string, object> claims,
        string providerName);

    ClaimsPrincipal MapToClaimsPrincipal(
        OidcUserInfo userInfo,
        string providerName);

    Dictionary<string, object> MapFromClaims(
        ClaimsPrincipal principal,
        string providerName);

    string MapClaimName(
        string originalClaimName,
        string providerName);

    object MapClaimValue(
        object originalValue,
        string claimName,
        string providerName);
}
