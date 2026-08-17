using Atelier.Framework.Primitives;
using System.Collections.Concurrent;
using Atelier.Framework.Attributes;
using Atelier.Framework.Identity.Configuration;
using Atelier.Framework.Identity.Interfaces;
using Atelier.Framework.Identity.Providers;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;
using Atelier.Framework.Resilience;



namespace Atelier.Framework.Identity.Services;

[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
[Infrastructure(InfrastructureLifetime.Singleton)]
public partial class OidcProviderFactory : IOidcProviderFactory, IAtelier, IDisposable
{
    [Runtime] private readonly OidcConfiguration _configuration = null!;
    [Requisite] protected readonly HttpClient _httpClient = null!;
    [Requisite] protected readonly IOidcClaimsMapper _claimsMapper = null!;
    [Requisite] protected readonly ResiliencePipelineFactory _resiliencePipelineFactory = null!;

    private readonly ConcurrentDictionary<string, IOidcProvider> _providers = new();

    public async Task<Outcome<IOidcProvider>> GetProviderAsync(
        string providerName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(providerName))
        {
            Observe(
                LogLevel.Warning,
                values: [("Event", "GetProviderFailed"), ("Reason", "Provider name cannot be null or empty")]);
            return Outcome<IOidcProvider>.Failure();
        }


        if (_providers.TryGetValue(providerName, out var existingProvider))
        {
            return Outcome<IOidcProvider>.Success(existingProvider);
        }

        var providerConfig = _configuration.GetProvider(providerName);
        if (providerConfig == null)
        {
            Observe(
                LogLevel.Warning,
                values: [("Event", "GetProviderFailed"), ("Reason", "OIDC provider is not configured"), ("ProviderName", providerName)]);
            return Outcome<IOidcProvider>.Failure();
        }

        var provider = await CreateProviderAsync(providerName, providerConfig, cancellationToken).ConfigureAwait(false);
        if (provider.IsSuccess && provider.Data != null)
        {
            if (!_providers.TryAdd(providerName, provider.Data))
            {
                if (provider.Data is IDisposable losingProvider)
                {
                    losingProvider.Dispose();
                }

                return Outcome<IOidcProvider>.Success(_providers[providerName]);
            }
        }

        return provider;
    }

    public async Task<Outcome<IOidcProvider>> GetDefaultProviderAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var defaultProviderName = _configuration.DefaultProvider;
            if (string.IsNullOrEmpty(defaultProviderName))
            {
                Observe(
                    LogLevel.Warning,
                    values: [("Event", "GetDefaultProviderFailed"), ("Reason", "No default OIDC provider configured")]);
                return Outcome<IOidcProvider>.Failure();
            }

            return await GetProviderAsync(defaultProviderName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Observe(LogLevel.Error, ex, values: [("Event", "GetDefaultProviderFailed"), ("Reason", "Failed to get default OIDC provider")]);

            return Outcome<IOidcProvider>.Failure();
        }
    }

    public async Task<Outcome<IEnumerable<IOidcProvider>>> GetAllProvidersAsync(
        CancellationToken cancellationToken = default)
    {
        var providers = new List<IOidcProvider>();

        foreach (var providerName in _configuration.Providers.Keys)
        {
            var providerResult = await GetProviderAsync(providerName, cancellationToken).ConfigureAwait(false);
            if (providerResult.IsSuccess && providerResult.Data != null)
            {
                providers.Add(providerResult.Data);
            }
        }

        return Outcome<IEnumerable<IOidcProvider>>.Success(providers);
    }

    public async Task<Outcome> IsProviderAvailableAsync(
        string providerName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            Observe(
                LogLevel.Warning,
                values: [("Event", "ProviderAvailabilityCheckFailed"), ("Reason", "Provider name cannot be null or whitespace")]);
            return Outcome.Failure();
        }


        var providerResult = await GetProviderAsync(providerName, cancellationToken).ConfigureAwait(false);
        if (!providerResult.IsSuccess || providerResult.Data == null)
        {
            Observe(
                LogLevel.Information,
                values: [("Event", "ProviderUnavailable"), ("Reason", "OIDC provider is not available"), ("ProviderName", providerName)]);
            return Outcome.Failure();
        }

        return Outcome.Success();
    }

    public async Task<Outcome> ResetProviderAsync(
        string providerName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            Observe(
                LogLevel.Warning,
                values: [("Event", "ResetProviderFailed"), ("Reason", "Provider name cannot be null or whitespace")]);
            return Outcome.Failure();
        }


        if (_providers.TryRemove(providerName, out var removed)
            && removed is IDisposable disposable)
        {
            disposable.Dispose();
        }

        return Outcome.Success();
    }

    public Task<Outcome> ResetAllProvidersAsync(
        CancellationToken cancellationToken = default)
    {
        foreach (var providerName in _providers.Keys)
        {
            if (_providers.TryRemove(providerName, out var provider)
                && provider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        return Task.FromResult(Outcome.Success());
    }

    private async Task<Outcome<IOidcProvider>> CreateProviderAsync(
        string providerName,
        OidcProviderConfiguration config,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentNullException.ThrowIfNull(config);

        try
        {
            Observe(LogLevel.Information, values: [("ProviderName", providerName), ("Authority", config.Authority), ("ClientId", config.ClientId)]);

            var provider = InstantiateProvider(
                providerName,
                config,
                _httpClient,
                _claimsMapper,
                _resiliencePipelineFactory,
                Logger);

            var validationResult = await provider.ValidateConfigurationAsync(cancellationToken).ConfigureAwait(false);
            if (!validationResult.IsSuccess)
            {
                if (provider is IDisposable disposableProvider)
                {
                    disposableProvider.Dispose();
                }

                Observe(
                    LogLevel.Warning,
                    values: [("Event", "ProviderCreationFailed"), ("Reason", "Provider configuration validation failed"), ("ProviderName", providerName)]);
                return Outcome<IOidcProvider>.Failure();
            }

            return Outcome<IOidcProvider>.Success(provider);
        }
        catch (Exception ex)
        {
            Observe(
                LogLevel.Error,
                ex,
                values: [("Event", "ProviderCreationFailed"), ("Reason", "Failed to create OIDC provider"), ("ProviderName", providerName)]);

            return Outcome<IOidcProvider>.Failure();
        }
    }

    private static GenericOidcProvider InstantiateProvider(
        string providerName,
        OidcProviderConfiguration config,
        HttpClient httpClient,
        IOidcClaimsMapper claimsMapper,
        ResiliencePipelineFactory resilience,
        ILogger? logger)
    {
        var kind = ResolveProviderKind(providerName, config);

        GenericOidcProvider provider = kind switch
        {
            OidcProviderKind.AzureAd => new AzureAdOidcProvider(httpClient, claimsMapper, resilience, logger),
            OidcProviderKind.Auth0 => new Auth0OidcProvider(httpClient, claimsMapper, resilience, logger),
            OidcProviderKind.Keycloak => new KeycloakOidcProvider(httpClient, claimsMapper, resilience, logger),
            _ => new GenericOidcProvider(httpClient, claimsMapper, resilience, logger)
        };

        return provider.Configure(providerName, config);
    }

    private static OidcProviderKind ResolveProviderKind(
        string providerName,
        OidcProviderConfiguration config)
    {
        var name = providerName.ToLowerInvariant();
        var authority = (config.Authority ?? string.Empty).ToLowerInvariant();

        if (name.Contains("azure")
            || name.Contains("entra")
            || authority.Contains("login.microsoftonline.com"))
        {
            return OidcProviderKind.AzureAd;
        }

        if (name.Contains("auth0")
            || authority.Contains(".auth0.com"))
        {
            return OidcProviderKind.Auth0;
        }

        if (name.Contains("keycloak")
            || authority.Contains("/realms/"))
        {
            return OidcProviderKind.Keycloak;
        }

        return OidcProviderKind.Generic;
    }

    private enum OidcProviderKind
    {
        Generic,
        AzureAd,
        Auth0,
        Keycloak
    }

    public void Dispose()
    {
        foreach (var providerName in _providers.Keys)
        {
            if (_providers.TryRemove(providerName, out var provider)
                && provider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
