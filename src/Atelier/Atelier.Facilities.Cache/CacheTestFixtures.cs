using Atelier.Framework.Testing;

namespace Atelier.Facilities.Cache;

[TestFixtureRegistry]
public static class CacheTestFixtures
{
    [Fixture(typeof(CacheKey))]
    public static CacheKey Key()
    {
        return new CacheKey
        {
            Key = "atelier:integration:user:42",
            Namespace = "sessions",
        };
    }

    [Fixture(typeof(CacheValue))]
    public static CacheValue Value()
    {
        return new CacheValue
        {
            Value = "{\"id\":42,\"name\":\"Atelier\"}",
            Ttl = TimeSpan.FromMinutes(5),
        };
    }

    [Fixture(typeof(CacheLookup))]
    public static CacheLookup Lookup()
    {
        return new CacheLookup
        {
            Found = true,
            Value = new CacheValue
            {
                Value = "{\"id\":42,\"name\":\"Atelier\"}",
                Ttl = TimeSpan.FromMinutes(5),
            },
        };
    }
}
