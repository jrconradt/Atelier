using Atelier.Framework.Primitives;
using Atelier.Framework.Attributes;
using Docker.DotNet;

namespace Atelier.Framework.Host.Execution;

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public sealed class DockerClientProvider : IDockerClientProvider, IDisposable
{
    private const string ENDPOINT_ENV = "ATELIER_DOCKER_HOST";
    private const string FALLBACK_ENDPOINT_ENV = "DOCKER_HOST";
    private const string ALLOW_INSECURE_ENV = "ATELIER_DOCKER_ALLOW_INSECURE_TCP";

    private readonly Lazy<IDockerClient> _client;

    public DockerClientProvider()
    {
        var endpoint = ResolveEndpoint();
        _client = new Lazy<IDockerClient>(() => CreateClient(endpoint));
    }

    public IDockerClient Client => _client.Value;

    public void Dispose()
    {
        if (_client.IsValueCreated)
        {
            _client.Value.Dispose();
        }
    }

    private static Uri? ResolveEndpoint()
    {
        var configured = Environment.GetEnvironmentVariable(ENDPOINT_ENV)
            ?? Environment.GetEnvironmentVariable(FALLBACK_ENDPOINT_ENV);

        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        if (!Uri.TryCreate(configured, UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException(
                $"Docker endpoint '{configured}' from '{ENDPOINT_ENV}'/'{FALLBACK_ENDPOINT_ENV}' is not a valid absolute URI. Use a 'unix://', 'npipe://', 'https://', or (with {ALLOW_INSECURE_ENV}=true) 'tcp://' endpoint.");
        }

        ValidateScheme(endpoint);
        return endpoint;
    }

    private static void ValidateScheme(Uri endpoint)
    {
        if (string.Equals(endpoint.Scheme, "unix", StringComparison.OrdinalIgnoreCase)
            || string.Equals(endpoint.Scheme, "npipe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(endpoint.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.Equals(endpoint.Scheme, "tcp", StringComparison.OrdinalIgnoreCase)
            || string.Equals(endpoint.Scheme, "http", StringComparison.OrdinalIgnoreCase))
        {
            if (IsInsecureTcpAllowed())
            {
                return;
            }

            throw new InvalidOperationException(
                $"Docker endpoint '{endpoint}' uses the plaintext '{endpoint.Scheme}' scheme. Use an 'https://' endpoint or a local 'unix://'/'npipe://' socket. Set {ALLOW_INSECURE_ENV}=true only for trusted local development.");
        }

        throw new InvalidOperationException(
            $"Docker endpoint scheme '{endpoint.Scheme}' is not supported. Use 'unix://', 'npipe://', 'https://', or (with {ALLOW_INSECURE_ENV}=true) 'tcp://'.");
    }

    private static bool IsInsecureTcpAllowed()
    {
        var flag = Environment.GetEnvironmentVariable(ALLOW_INSECURE_ENV);
        return bool.TryParse(flag, out var allowed)
            && allowed;
    }

    private static IDockerClient CreateClient(Uri? endpoint)
    {
        var configuration = endpoint is null
            ? new DockerClientConfiguration()
            : new DockerClientConfiguration(endpoint);

        try
        {
            return configuration.CreateClient();
        }
        catch (Exception ex)
        {
            configuration.Dispose();

            throw new InvalidOperationException(
                $"Failed to create a Docker client for endpoint '{endpoint?.ToString() ?? "default"}': {ex.Message}",
                ex);
        }
    }
}
