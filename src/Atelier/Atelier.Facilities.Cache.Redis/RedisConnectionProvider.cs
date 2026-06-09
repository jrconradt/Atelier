using Atelier.Framework.Primitives;
using System.Net;
using Atelier.Framework.Attributes;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using StackExchange.Redis;

namespace Atelier.Facilities.Cache.Redis;

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class RedisConnectionProvider : IRedisConnectionProvider, IDisposable, IAtelier
{
    private const string PASSWORD_ENV = "ATELIER_REDIS_PASSWORD";

    private readonly bool _requireTlsForRemote = true;
    private readonly StateCell _cell = new();

    public bool IsConfigured
    {
        get
        {
            return _cell.Read().Multiplexer is not null;
        }
    }

    public bool IsConnected
    {
        get
        {
            return _cell.Read().Multiplexer is { IsConnected: true };
        }
    }

    public Outcome<IRedisConnectionProvider> Configure(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string cannot be empty.", nameof(connectionString));
        }

        IConnectionMultiplexer created = ConnectionMultiplexer.Connect(BuildOptions(connectionString));

        while (true)
        {
            ConnectionState current = _cell.Read();

            if (current.Disposed)
            {
                created.Dispose();
                Observe(LogLevel.Warning, values: [("Reason", "Configure called after the connection provider was disposed")]);
                return Outcome<IRedisConnectionProvider>.Failure();
            }

            if (string.Equals(connectionString, current.ConnectionString, StringComparison.Ordinal)
                && current.Multiplexer is not null)
            {
                created.Dispose();
                return Outcome<IRedisConnectionProvider>.Success(this);
            }

            ConnectionState next = current.WithConnection(connectionString, created);
            if (_cell.TrySwap(current, next))
            {
                DrainAndClose(current.Multiplexer);
                return Outcome<IRedisConnectionProvider>.Success(this);
            }
        }
    }

    public async Task<(string? Value, TimeSpan? Ttl)> StringGetAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var database = ResolveDatabase();
        var stored = await database.StringGetWithExpiryAsync(key).ConfigureAwait(false);
        if (stored.Value.IsNull)
        {
            return (null, null);
        }

        return (stored.Value.ToString(), stored.Expiry);
    }

    public async Task<bool> StringSetAsync(string key,
                                           string value,
                                           TimeSpan? expiry,
                                           CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await ResolveDatabase().StringSetAsync(key,
                                                      value,
                                                      expiry).ConfigureAwait(false);
    }

    public async Task<bool> KeyDeleteAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await ResolveDatabase().KeyDeleteAsync(key).ConfigureAwait(false);
    }

    private IDatabase ResolveDatabase()
    {
        ConnectionState current = _cell.Read();

        if (current.Disposed)
        {
            throw new ObjectDisposedException(nameof(RedisConnectionProvider));
        }

        if (current.Multiplexer is null)
        {
            throw new InvalidOperationException("RedisConnectionProvider must be configured before use; call Configure with a connection string.");
        }

        return current.Multiplexer.GetDatabase();
    }

    private void DrainAndClose(IConnectionMultiplexer? multiplexer)
    {
        if (multiplexer is null)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await multiplexer.CloseAsync(allowCommandsToComplete: true).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Observe(LogLevel.Warning, ex, values: [("Event", "Redis connection drain failed during graceful close")]);
            }
            finally
            {
                multiplexer.Dispose();
            }
        });
    }

    private ConfigurationOptions BuildOptions(string connectionString)
    {
        var options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = false;
        options.ConnectRetry = 3;
        options.ConnectTimeout = 5000;

        var envPassword = Environment.GetEnvironmentVariable(PASSWORD_ENV);
        if (string.IsNullOrEmpty(options.Password)
            && !string.IsNullOrEmpty(envPassword))
        {
            options.Password = envPassword;
        }

        if (_requireTlsForRemote && !options.Ssl
            && !AllEndpointsLoopback(options))
        {
            options.Ssl = true;
        }

        return options;
    }

    private static bool AllEndpointsLoopback(ConfigurationOptions options)
    {
        if (options.EndPoints.Count == 0)
        {
            return false;
        }

        foreach (var endpoint in options.EndPoints)
        {
            if (!IsLoopbackEndpoint(endpoint))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLoopbackEndpoint(EndPoint endpoint)
    {
        if (endpoint is DnsEndPoint dns)
        {
            if (string.Equals(dns.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return IPAddress.TryParse(dns.Host, out var parsed)
                && IPAddress.IsLoopback(parsed);
        }

        if (endpoint is IPEndPoint ip)
        {
            return IPAddress.IsLoopback(ip.Address);
        }

        return false;
    }

    public void Dispose()
    {
        while (true)
        {
            ConnectionState current = _cell.Read();
            if (current.Disposed)
            {
                return;
            }

            ConnectionState next = current.AsDisposed();
            if (_cell.TrySwap(current, next))
            {
                current.Multiplexer?.Dispose();
                return;
            }
        }
    }

    private sealed class StateCell
    {
        private ConnectionState _value = ConnectionState.Empty;

        public ConnectionState Read()
        {
            return Volatile.Read(ref _value);
        }

        public bool TrySwap(ConnectionState expected, ConnectionState replacement)
        {
            return ReferenceEquals(Interlocked.CompareExchange(ref _value, replacement, expected), expected);
        }
    }

    private sealed class ConnectionState
    {
        public static readonly ConnectionState Empty = new(null, null, false);

        private ConnectionState(string? connectionString,
                                IConnectionMultiplexer? multiplexer,
                                bool disposed)
        {
            ConnectionString = connectionString;
            Multiplexer = multiplexer;
            Disposed = disposed;
        }

        public string? ConnectionString { get; }
        public IConnectionMultiplexer? Multiplexer { get; }
        public bool Disposed { get; }

        public ConnectionState WithConnection(string connectionString,
                                              IConnectionMultiplexer multiplexer)
        {
            return new ConnectionState(connectionString, multiplexer, Disposed);
        }

        public ConnectionState AsDisposed()
        {
            return new ConnectionState(ConnectionString, null, true);
        }
    }
}
