namespace Atelier.Build.Generation;

public static class InfrastructurePolicy
{
    public const string PostgresImage = "postgres:17-alpine";
    public const string PostgresUser = "atelier";
    public const string PostgresDatabase = "atelier";
    public const int PostgresPort = 5432;
    public const string PostgresVolume = "postgres-data";

    public const string RedisImage = "redis:7-alpine";
    public const int RedisPort = 6379;
    public const string RedisVolume = "redis-data";
}
