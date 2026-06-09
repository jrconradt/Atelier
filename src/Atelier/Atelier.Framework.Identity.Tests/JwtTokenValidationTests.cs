using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Atelier.Framework.Identity.Configuration;
using Atelier.Framework.Identity.Services;
using Atelier.Framework.Testing;
using ILogger = Atelier.Framework.Observability.ILogger;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Atelier.Framework.Identity.Tests;

public sealed class JwtTokenValidationTests
{
    private const string KeyA = "atelier-signing-key-aaaaaaaaaaaaaaaaaaaaaaaa";
    private const string KeyB = "atelier-signing-key-bbbbbbbbbbbbbbbbbbbbbbbb";

    private static JwtAuthenticationOptions Options(
        string signingKey,
        string issuer = "atelier",
        string audience = "atelier",
        TimeSpan? lifetime = null)
        => new()
        {
            SigningKey = signingKey,
            Issuer = issuer,
            Audience = audience,
            ClockSkew = TimeSpan.Zero,
            TokenLifetime = lifetime ?? TimeSpan.FromHours(1),
        };

    private static readonly DateTimeOffset FixedInstant = new(
        2026,
        1,
        1,
        0,
        0,
        0,
        TimeSpan.Zero);

    private static TimeProvider FixedClock()
        => new FixedTimeProvider(FixedInstant);

    private static JwtTokenIssuer Issuer(
        JwtAuthenticationOptions options,
        TimeProvider timeProvider)
        => new(Microsoft.Extensions.Options.Options.Create(options),
               timeProvider,
               AutoMockProvider.For<ILogger>());

    private static JwtTokenValidator Validator(
        JwtAuthenticationOptions options,
        TimeProvider timeProvider)
        => new(Microsoft.Extensions.Options.Options.Create(options),
               timeProvider,
               AutoMockProvider.For<ILogger>());

    private static JwtTokenIssuer Issuer(JwtAuthenticationOptions options)
        => Issuer(options, FixedClock());

    private static JwtTokenValidator Validator(JwtAuthenticationOptions options)
        => Validator(options, FixedClock());

    [Fact]
    public void Validate_AcceptsFreshlyIssuedToken()
    {
        var options = Options(KeyA);
        var clock = FixedClock();
        var issued = Issuer(options, clock).Issue("user-1");
        Assert.True(issued.IsSuccess);

        var result = Validator(options, clock).Validate(issued.Data!);

        Assert.True(result.IsSuccess);
        Assert.Equal("user-1", result.Data!.FindFirst("sub")?.Value);
    }

    private static string ExpiredToken(JwtAuthenticationOptions options)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var notBefore = FixedInstant.UtcDateTime.AddMinutes(-10);
        var expires = FixedInstant.UtcDateTime.AddMinutes(-5);
        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: new[] { new System.Security.Claims.Claim(JwtRegisteredClaimNames.Sub, "user-1") },
            notBefore: notBefore,
            expires: expires,
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public void Validate_RejectsExpiredToken()
    {
        var options = Options(KeyA);
        var token = ExpiredToken(options);

        var result = Validator(options).Validate(token);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Validate_RejectsWrongIssuer()
    {
        var issued = Issuer(Options(KeyA, issuer: "intruder")).Issue("user-1");
        Assert.True(issued.IsSuccess);

        var result = Validator(Options(KeyA, issuer: "atelier")).Validate(issued.Data!);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Validate_RejectsWrongAudience()
    {
        var issued = Issuer(Options(KeyA, audience: "other-service")).Issue("user-1");
        Assert.True(issued.IsSuccess);

        var result = Validator(Options(KeyA, audience: "atelier")).Validate(issued.Data!);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Validate_RejectsTokenSignedWithRotatedAwayKey()
    {
        var issued = Issuer(Options(KeyA)).Issue("user-1");
        Assert.True(issued.IsSuccess);

        var result = Validator(Options(KeyB)).Validate(issued.Data!);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Validate_AcceptsTokenAfterKeyRotatedBackToIssuingKey()
    {
        var issued = Issuer(Options(KeyA)).Issue("user-1");
        Assert.True(issued.IsSuccess);

        Assert.False(Validator(Options(KeyB)).Validate(issued.Data!).IsSuccess);
        Assert.True(Validator(Options(KeyA)).Validate(issued.Data!).IsSuccess);
    }

    [Fact]
    public void Validate_RejectsEmptyToken()
    {
        var result = Validator(Options(KeyA)).Validate("   ");

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void OptionsValidation_RejectsTooShortSigningKey()
    {
        var result = new JwtAuthenticationOptionsValidator().Validate(null, Options("too-short"));

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!,
                        failure => failure.Contains("at least 32 bytes", StringComparison.Ordinal));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _instant;

        public FixedTimeProvider(DateTimeOffset instant)
        {
            _instant = instant;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _instant;
        }
    }
}
