using Atelier.Framework.Primitives;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using Atelier.Framework.Attributes;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Observability;
using Atelier.Framework.Identity.Interfaces;

namespace Atelier.Framework.Identity.Services;

[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
[Infrastructure(InfrastructureLifetime.Singleton)]
public partial class OidcTokenIssuer : IOidcTokenIssuer, IAtelier
{
    private readonly RSA _rsa;
    private readonly RsaSecurityKey _signingKey;
    private readonly string _kid = "atelier-identity-service-key-v1";

    public OidcTokenIssuer()
    {
        _rsa = RSA.Create(2048);
        _signingKey = new RsaSecurityKey(_rsa)
        {
            KeyId = _kid
        };
    }

    public Outcome<string> GetJwksJson()
    {
        try
        {
            var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(_signingKey);
            jwk.Use = "sig";
            jwk.Alg = SecurityAlgorithms.RsaSha256;

            var jwks = new
            {
                keys = new[]
                {
                    new
                    {
                        kty = jwk.Kty,
                        use = jwk.Use,
                        alg = jwk.Alg,
                        kid = jwk.Kid,
                        n = jwk.N,
                        e = jwk.E
                    }
                }
            };

            var json = System.Text.Json.JsonSerializer.Serialize(jwks);
            return Outcome<string>.Success(json);
        }
        catch (Exception ex)
        {
            Observe(LogLevel.Error, exception: ex, values: [("Event", "GetJwksFailed")]);
            return Outcome<string>.Failure();
        }
    }

    public Outcome<string> IssueAccessToken(
        string issuer,
        string clientId,
        string? subject = null,
        IEnumerable<string>? scopes = null,
        IEnumerable<string>? roles = null,
        TimeSpan? lifetime = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        try
        {
            var credentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256);
            var now = DateTime.UtcNow;
            var expires = now.Add(lifetime ?? TimeSpan.FromHours(1));

            var claimsList = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Iss, issuer),
                new(JwtRegisteredClaimNames.Aud, clientId),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new("client_id", clientId)
            };

            if (!string.IsNullOrEmpty(subject))
            {
                claimsList.Add(new(JwtRegisteredClaimNames.Sub, subject));
            }

            if (scopes is not null)
            {
                claimsList.Add(new("scope", string.Join(" ", scopes)));
            }

            if (roles is not null)
            {
                foreach (var role in roles)
                {
                    claimsList.Add(new("roles", role));
                }
            }

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: clientId,
                claims: claimsList,
                notBefore: now,
                expires: expires,
                signingCredentials: credentials);

            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.WriteToken(token);

            Observe(LogLevel.Information, values: [
                ("Event", "AccessTokenIssued"),
                ("ClientId", clientId),
                ("Subject", subject ?? "none"),
                ("Issuer", issuer)
            ]);

            return Outcome<string>.Success(jwt);
        }
        catch (Exception ex)
        {
            Observe(LogLevel.Error, exception: ex, values: [
                ("Event", "IssueAccessTokenFailed"),
                ("ClientId", clientId),
                ("Subject", subject ?? "none")
            ]);
            return Outcome<string>.Failure();
        }
    }

    public Outcome<string> IssueIdToken(
        string issuer,
        string clientId,
        string subject,
        string username,
        TimeSpan? lifetime = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        try
        {
            var credentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256);
            var now = DateTime.UtcNow;
            var expires = now.Add(lifetime ?? TimeSpan.FromHours(1));

            var claimsList = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Iss, issuer),
                new(JwtRegisteredClaimNames.Aud, clientId),
                new(JwtRegisteredClaimNames.Sub, subject),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new("preferred_username", username),
                new("name", username)
            };

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: clientId,
                claims: claimsList,
                notBefore: now,
                expires: expires,
                signingCredentials: credentials);

            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.WriteToken(token);

            Observe(LogLevel.Information, values: [
                ("Event", "IdTokenIssued"),
                ("ClientId", clientId),
                ("Subject", subject),
                ("Username", username)
            ]);

            return Outcome<string>.Success(jwt);
        }
        catch (Exception ex)
        {
            Observe(LogLevel.Error, exception: ex, values: [
                ("Event", "IssueIdTokenFailed"),
                ("ClientId", clientId),
                ("Subject", subject),
                ("Username", username)
            ]);
            return Outcome<string>.Failure();
        }
    }
}
