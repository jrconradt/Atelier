using Atelier.Framework.Outcomes;

namespace Atelier.Facilities.Cache.Redis;

public interface IRedisConnectionProvider
{
    bool IsConfigured { get; }

    bool IsConnected { get; }

    Outcome<IRedisConnectionProvider> Configure(string connectionString);

    Task<(string? Value, TimeSpan? Ttl)> StringGetAsync(
        string key,
        CancellationToken cancellationToken = default);

    Task<bool> StringSetAsync(string key,
                              string value,
                              TimeSpan? expiry,
                              CancellationToken cancellationToken = default);

    Task<bool> KeyDeleteAsync(
        string key,
        CancellationToken cancellationToken = default);
}
