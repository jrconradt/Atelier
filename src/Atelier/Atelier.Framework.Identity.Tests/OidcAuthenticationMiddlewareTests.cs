using System.Security.Claims;
using Atelier.Framework.Context;
using Atelier.Framework.Identity.Interfaces;
using Atelier.Framework.Identity.Middleware;
using Atelier.Framework.Identity.Models;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Atelier.Framework.Identity.Tests;

public sealed class OidcAuthenticationMiddlewareTests
{
    private sealed class RejectingTokenService : IOidcTokenService
    {
        public Task<Outcome<ClaimsPrincipal>> ValidateTokenAsync(string token, string? providerName = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome<ClaimsPrincipal>.Failure());

        public Task<Outcome<OidcUserInfo>> ExtractUserInfoAsync(string token, string? providerName = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome<OidcUserInfo>.Failure());

        public Task<Outcome<OidcTokenResult>> RefreshTokenAsync(string refreshToken, string? providerName = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome<OidcTokenResult>.Failure());

        public Task<Outcome> IsTokenValidAsync(string token, string? providerName = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome.Failure());

        public Task<Outcome> RevokeTokenAsync(string token, string? providerName = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome.Success());

        public Task<Outcome<Dictionary<string, object>>> ExtractClaimsAsync(string token, string? providerName = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome<Dictionary<string, object>>.Failure());
    }

    private sealed class NoProviderFactory : IOidcProviderFactory
    {
        public Task<Outcome<IOidcProvider>> GetProviderAsync(string providerName, CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome<IOidcProvider>.Failure());

        public Task<Outcome<IOidcProvider>> GetDefaultProviderAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome<IOidcProvider>.Failure());

        public Task<Outcome<IEnumerable<IOidcProvider>>> GetAllProvidersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome<IEnumerable<IOidcProvider>>.Success(Array.Empty<IOidcProvider>()));

        public Task<Outcome> IsProviderAvailableAsync(string providerName, CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome.Failure());

        public Task<Outcome> ResetProviderAsync(string providerName, CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome.Success());

        public Task<Outcome> ResetAllProvidersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome.Success());
    }

    private sealed class AcceptingProvider : IOidcProvider
    {
        public string ProviderName => "test";

        public string Authority => "https://issuer.test";

        public bool IsConfigured => true;

        public Task<Outcome<OidcTokenResult>> AuthenticateAsync(OidcAuthorizationCodeExchange exchange, CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome<OidcTokenResult>.Failure());

        public Task<Outcome<OidcTokenResult>> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome<OidcTokenResult>.Failure());

        public Task<Outcome<OidcUserInfo>> GetUserInfoAsync(string accessToken, CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome<OidcUserInfo>.Failure());

        public Task<Outcome> ValidateTokenAsync(string token, CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome.Success());

        public Task<Outcome<string>> GetAuthorizationUrlAsync(string? state = null, string? nonce = null, string? codeChallenge = null, string? codeChallengeMethod = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome<string>.Failure());

        public Task<Outcome> RevokeTokenAsync(string token, CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome.Success());

        public Task<Outcome> LogoutAsync(string? idToken = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome.Success());
    }

    private sealed class RejectingValidationProvider : IOidcProvider
    {
        public string ProviderName => "test";

        public string Authority => "https://issuer.test";

        public bool IsConfigured => true;

        public Task<Outcome<OidcTokenResult>> AuthenticateAsync(OidcAuthorizationCodeExchange exchange, CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome<OidcTokenResult>.Failure());

        public Task<Outcome<OidcTokenResult>> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome<OidcTokenResult>.Failure());

        public Task<Outcome<OidcUserInfo>> GetUserInfoAsync(string accessToken, CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome<OidcUserInfo>.Failure());

        public Task<Outcome> ValidateTokenAsync(string token, CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome.Failure());

        public Task<Outcome<string>> GetAuthorizationUrlAsync(string? state = null, string? nonce = null, string? codeChallenge = null, string? codeChallengeMethod = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome<string>.Failure());

        public Task<Outcome> RevokeTokenAsync(string token, CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome.Success());

        public Task<Outcome> LogoutAsync(string? idToken = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome.Success());
    }

    private sealed class RejectingValidationProviderFactory : IOidcProviderFactory
    {
        public Task<Outcome<IOidcProvider>> GetProviderAsync(string providerName, CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome<IOidcProvider>.Success(new RejectingValidationProvider()));

        public Task<Outcome<IOidcProvider>> GetDefaultProviderAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome<IOidcProvider>.Success(new RejectingValidationProvider()));

        public Task<Outcome<IEnumerable<IOidcProvider>>> GetAllProvidersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome<IEnumerable<IOidcProvider>>.Success(new IOidcProvider[] { new RejectingValidationProvider() }));

        public Task<Outcome> IsProviderAvailableAsync(string providerName, CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome.Success());

        public Task<Outcome> ResetProviderAsync(string providerName, CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome.Success());

        public Task<Outcome> ResetAllProvidersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome.Success());
    }

    private sealed class AcceptingProviderFactory : IOidcProviderFactory
    {
        public Task<Outcome<IOidcProvider>> GetProviderAsync(string providerName, CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome<IOidcProvider>.Success(new AcceptingProvider()));

        public Task<Outcome<IOidcProvider>> GetDefaultProviderAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome<IOidcProvider>.Success(new AcceptingProvider()));

        public Task<Outcome<IEnumerable<IOidcProvider>>> GetAllProvidersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome<IEnumerable<IOidcProvider>>.Success(new IOidcProvider[] { new AcceptingProvider() }));

        public Task<Outcome> IsProviderAvailableAsync(string providerName, CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome.Success());

        public Task<Outcome> ResetProviderAsync(string providerName, CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome.Success());

        public Task<Outcome> ResetAllProvidersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome.Success());
    }

    private sealed class ClaimsTokenService : IOidcTokenService
    {
        private readonly Dictionary<string, object> _claims;

        public ClaimsTokenService(Dictionary<string, object> claims)
        {
            _claims = claims;
        }

        public Task<Outcome<ClaimsPrincipal>> ValidateTokenAsync(string token, string? providerName = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome<ClaimsPrincipal>.Success(new ClaimsPrincipal(new ClaimsIdentity("test"))));

        public Task<Outcome<OidcUserInfo>> ExtractUserInfoAsync(string token, string? providerName = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome<OidcUserInfo>.Failure());

        public Task<Outcome<OidcTokenResult>> RefreshTokenAsync(string refreshToken, string? providerName = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome<OidcTokenResult>.Failure());

        public Task<Outcome> IsTokenValidAsync(string token, string? providerName = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome.Success());

        public Task<Outcome> RevokeTokenAsync(string token, string? providerName = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome.Success());

        public Task<Outcome<Dictionary<string, object>>> ExtractClaimsAsync(string token, string? providerName = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Outcome<Dictionary<string, object>>.Success(_claims));
    }

    private static (OidcAuthenticationMiddleware middleware, DefaultHttpContext context) Build(OidcAuthenticationOptions options)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(options));
        services.AddSingleton<IOidcTokenService, RejectingTokenService>();
        services.AddSingleton<IOidcProviderFactory, NoProviderFactory>();
        services.AddSingleton<ContextManager>();
        services.AddSingleton(AutoMockProvider.For<ILogger>());
        services.AddScoped<OidcAuthenticationMiddleware>();

        var provider = services.BuildServiceProvider();
        var middleware = provider.GetRequiredService<OidcAuthenticationMiddleware>();

        var context = new DefaultHttpContext
        {
            RequestServices = provider,
        };
        context.Response.Body = new MemoryStream();
        return (middleware, context);
    }

    private static (OidcAuthenticationMiddleware middleware, DefaultHttpContext context, ContextManager contextManager) BuildWithClaims(
        OidcAuthenticationOptions options,
        Dictionary<string, object> claims)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(options));
        services.AddSingleton<IOidcTokenService>(new ClaimsTokenService(claims));
        services.AddSingleton<IOidcProviderFactory, AcceptingProviderFactory>();
        services.AddSingleton<ContextManager>();
        services.AddSingleton(AutoMockProvider.For<ILogger>());
        services.AddScoped<OidcAuthenticationMiddleware>();

        var provider = services.BuildServiceProvider();
        var middleware = provider.GetRequiredService<OidcAuthenticationMiddleware>();
        var contextManager = provider.GetRequiredService<ContextManager>();

        var context = new DefaultHttpContext
        {
            RequestServices = provider,
        };
        context.Response.Body = new MemoryStream();
        return (middleware, context, contextManager);
    }

    private static (OidcAuthenticationMiddleware middleware, DefaultHttpContext context) BuildWithRejectedValidation(
        OidcAuthenticationOptions options,
        Dictionary<string, object> claims)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(options));
        services.AddSingleton<IOidcTokenService>(new ClaimsTokenService(claims));
        services.AddSingleton<IOidcProviderFactory, RejectingValidationProviderFactory>();
        services.AddSingleton<ContextManager>();
        services.AddSingleton(AutoMockProvider.For<ILogger>());
        services.AddScoped<OidcAuthenticationMiddleware>();

        var provider = services.BuildServiceProvider();
        var middleware = provider.GetRequiredService<OidcAuthenticationMiddleware>();

        var context = new DefaultHttpContext
        {
            RequestServices = provider,
        };
        context.Response.Body = new MemoryStream();
        return (middleware, context);
    }

    [Fact]
    public async Task SkipsAuthentication_WhenDisabled_AndCallsNext()
    {
        var options = new OidcAuthenticationOptions
        {
            EnableAuthentication = false,
        };
        var (middleware, context) = Build(options);
        var nextCalled = false;

        await middleware
            .Configure(_ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            })
            .InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.NotEqual(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task SkipsAuthentication_OnExcludedPath_AndCallsNext()
    {
        var options = new OidcAuthenticationOptions
        {
            EnableAuthentication = true,
            ExcludedPaths = ["/health"],
        };
        var (middleware, context) = Build(options);
        context.Request.Path = "/health/ready";
        var nextCalled = false;

        await middleware
            .Configure(_ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            })
            .InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.NotEqual(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task Returns401_WhenTokenMissing_AndAuthenticationRequired()
    {
        var options = new OidcAuthenticationOptions
        {
            EnableAuthentication = true,
            RequireAuthentication = true,
            ExcludedPaths = [],
        };
        var (middleware, context) = Build(options);
        context.Request.Path = "/orders";
        var nextCalled = false;

        await middleware
            .Configure(_ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            })
            .InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task CallsNext_WhenTokenMissing_AndAuthenticationOptional()
    {
        var options = new OidcAuthenticationOptions
        {
            EnableAuthentication = true,
            RequireAuthentication = false,
            ExcludedPaths = [],
        };
        var (middleware, context) = Build(options);
        context.Request.Path = "/orders";
        var nextCalled = false;

        await middleware
            .Configure(_ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            })
            .InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.NotEqual(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task FinalizesRequestContext_AfterPipeline()
    {
        var options = new OidcAuthenticationOptions
        {
            EnableAuthentication = true,
            RequireAuthentication = true,
            ProviderName = "test",
            ServiceId = "orders-service",
            ExcludedPaths = [],
        };
        var claims = new Dictionary<string, object>
        {
            ["sub"] = "user-1",
            ["exp"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
        };
        var (middleware, context, _) = BuildWithClaims(options, claims);
        context.Request.Path = "/orders";
        context.Request.Headers.Authorization = "Bearer some.bearer.token";

        await middleware
            .Configure(_ => Task.CompletedTask)
            .InvokeAsync(context);

        var requestContext = Assert.IsAssignableFrom<IContext>(context.Items["AtelierContext"]);
        Assert.Equal(ContextLifecycle.Completed, requestContext.Lifecycle);
    }

    [Fact]
    public async Task Returns401_WhenSubjectClaimMissing_AndAuthenticationRequired()
    {
        var options = new OidcAuthenticationOptions
        {
            EnableAuthentication = true,
            RequireAuthentication = true,
            ProviderName = "test",
            ExcludedPaths = [],
        };
        var claims = new Dictionary<string, object>
        {
            ["name"] = "No Subject",
            ["exp"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
        };
        var (middleware, context, _) = BuildWithClaims(options, claims);
        context.Request.Path = "/orders";
        context.Request.Headers.Authorization = "Bearer some.bearer.token";
        var nextCalled = false;

        await middleware
            .Configure(_ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            })
            .InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task Returns401_WhenTokenRejected_AndAuthenticationRequired()
    {
        var options = new OidcAuthenticationOptions
        {
            EnableAuthentication = true,
            RequireAuthentication = true,
            ProviderName = "test",
            ExcludedPaths = [],
        };
        var (middleware, context) = Build(options);
        context.Request.Path = "/orders";
        context.Request.Headers.Authorization = "Bearer some.bearer.token";
        var nextCalled = false;

        await middleware
            .Configure(_ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            })
            .InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task Returns401_WhenValidationFails_WithClaimsPresent_AndAuthenticationRequired()
    {
        var options = new OidcAuthenticationOptions
        {
            EnableAuthentication = true,
            RequireAuthentication = true,
            ProviderName = "test",
            ExcludedPaths = [],
        };
        var claims = new Dictionary<string, object>
        {
            ["sub"] = "user-1",
            ["exp"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
        };
        var (middleware, context) = BuildWithRejectedValidation(options, claims);
        context.Request.Path = "/orders";
        context.Request.Headers.Authorization = "Bearer some.bearer.token";
        var nextCalled = false;

        await middleware
            .Configure(_ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            })
            .InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(401, context.Response.StatusCode);
    }
}
