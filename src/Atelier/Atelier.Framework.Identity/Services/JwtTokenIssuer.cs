using Atelier.Framework.Primitives;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Atelier.Framework.Identity.Configuration;
using Atelier.Framework.Identity.Interfaces;
using Atelier.Framework.Attributes;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Atelier.Framework.Identity.Services;

[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
[Infrastructure(InfrastructureLifetime.Singleton)]
public partial class JwtTokenIssuer : IJwtTokenIssuer, IAtelier
{
    [Requisite] private readonly IOptions<JwtAuthenticationOptions> _optionsAccessor = null!;
    [Requisite] private readonly TimeProvider _timeProvider = null!;

    private readonly JwtSecurityTokenHandler _handler = new();

    private JwtAuthenticationOptions _options => _optionsAccessor.Value;

    public Outcome<string> Issue(
        string subject,
        IEnumerable<Claim>? claims = null,
        TimeSpan? lifetime = null)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            Observe(
                LogLevel.Warning,
                values: [("Event", "TokenIssueFailed"), ("Reason", "Subject cannot be empty")]);
            return Outcome<string>.Failure();
        }

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "Subject", subject);

        if (string.IsNullOrWhiteSpace(_options.SigningKey))
        {
            Observe(
                LogLevel.Error,
                values: [("Event", "TokenIssueFailed"), ("Reason", "Signing key is not configured"), ("Subject", subject)]);
            return Outcome<string>.Failure();
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var jti = Guid.NewGuid().ToString();
        var expires = now.Add(lifetime ?? _options.TokenLifetime);

        var tokenClaims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject),
            new(JwtRegisteredClaimNames.Jti, jti)
        };

        if (claims is not null)
        {
            tokenClaims.AddRange(claims);
        }

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: tokenClaims,
            notBefore: now,
            expires: expires,
            signingCredentials: credentials);

        Observe(LogLevel.Information, values: [("Event", "TokenIssued"), ("Subject", subject), ("Jti", jti), ("Issuer", _options.Issuer), ("Audience", _options.Audience), ("ExpiresAt", expires)]);

        return Outcome<string>.Success(_handler.WriteToken(token));
    }
}
