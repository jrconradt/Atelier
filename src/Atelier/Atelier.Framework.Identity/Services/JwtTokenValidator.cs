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
public partial class JwtTokenValidator : IJwtTokenValidator, IAtelier
{
    [Requisite] private readonly IOptions<JwtAuthenticationOptions> _optionsAccessor = null!;
    [Requisite] private readonly TimeProvider _timeProvider = null!;

    private readonly JwtSecurityTokenHandler _handler = new()
    {
        MapInboundClaims = false
    };

    private static readonly string[] ValidSigningAlgorithms = new[] { SecurityAlgorithms.HmacSha256 };

    private JwtAuthenticationOptions _options => _optionsAccessor.Value;

    private readonly ParameterCache _cache = new();

    private sealed class ParameterCache
    {
        public TokenValidationParameters? Parameters;
        public JwtAuthenticationOptions? Options;
    }

    private TokenValidationParameters ValidationParameters()
    {
        var options = _options;
        var cached = Volatile.Read(ref _cache.Parameters);
        if (cached is not null
            && ReferenceEquals(Volatile.Read(ref _cache.Options), options))
        {
            return cached;
        }

        var built = BuildValidationParameters(options, _timeProvider);
        Volatile.Write(ref _cache.Options, options);
        Volatile.Write(ref _cache.Parameters, built);
        return built;
    }

    private static TokenValidationParameters BuildValidationParameters(
        JwtAuthenticationOptions options,
        TimeProvider timeProvider)
    {
        return new TokenValidationParameters
        {
            ValidateIssuer = options.ValidateIssuer,
            ValidIssuer = options.Issuer,
            ValidateAudience = options.ValidateAudience,
            ValidAudience = options.Audience,
            ValidateLifetime = options.ValidateLifetime,
            ValidateIssuerSigningKey = options.ValidateSigningKey,
            RequireSignedTokens = true,
            ClockSkew = options.ClockSkew,
            ValidAlgorithms = ValidSigningAlgorithms,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
            LifetimeValidator = (notBefore, expires, securityToken, validationParameters) =>
                ValidateLifetime(
                    notBefore,
                    expires,
                    options.ClockSkew,
                    timeProvider.GetUtcNow().UtcDateTime)
        };
    }

    private static bool ValidateLifetime(
        DateTime? notBefore,
        DateTime? expires,
        TimeSpan clockSkew,
        DateTime now)
    {
        if (notBefore.HasValue
            && now + clockSkew < notBefore.Value)
        {
            throw new SecurityTokenNotYetValidException($"Token is not valid before {notBefore.Value:O}");
        }

        if (expires.HasValue
            && now - clockSkew > expires.Value)
        {
            throw new SecurityTokenExpiredException($"Token expired at {expires.Value:O}");
        }

        return true;
    }

    public TokenValidationParameters CreateValidationParameters()
    {
        return ValidationParameters();
    }

    public Outcome<ClaimsPrincipal> Validate(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            Observe(
                LogLevel.Warning,
                values: [("Event", "TokenValidationFailed"), ("Reason", "Token cannot be empty")]);

            return Outcome<ClaimsPrincipal>.Failure();
        }

        try
        {
            var principal = _handler.ValidateToken(token, ValidationParameters(), out _);
            return Outcome<ClaimsPrincipal>.Success(principal);
        }
        catch (SecurityTokenExpiredException)
        {
            Observe(
                LogLevel.Warning,
                values: [("Event", "TokenValidationFailed"), ("Reason", "Token has expired")]);

            return Outcome<ClaimsPrincipal>.Failure();
        }
        catch (SecurityTokenException ex)
        {
            Observe(
                LogLevel.Warning,
                ex,
                values: [("Event", "TokenValidationFailed"), ("Reason", "Token validation failed")]);

            return Outcome<ClaimsPrincipal>.Failure();
        }
    }
}
