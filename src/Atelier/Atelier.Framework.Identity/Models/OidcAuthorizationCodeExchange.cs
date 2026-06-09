using Atelier.Framework.Attributes;
using System.Text.Json.Serialization;

namespace Atelier.Framework.Identity.Models;

[ContractAttribute("OidcAuthorizationCodeExchange", Version = "1.0")]
public sealed class OidcAuthorizationCodeExchange
{
    [JsonIgnore]
    public required string AuthorizationCode { get; init; }

    [JsonIgnore]
    public string? CodeVerifier { get; init; }

    public string? ReturnedState { get; init; }

    public string? ExpectedState { get; init; }

    public string? ExpectedNonce { get; init; }
}
