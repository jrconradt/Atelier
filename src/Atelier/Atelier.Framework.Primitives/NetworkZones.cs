namespace Atelier.Framework.Primitives;

[ZonePolicy(
    AllowedInbound = new[] { typeof(External), typeof(Application), typeof(Management) },
    AllowedOutbound = new[] { typeof(Application), typeof(Data) },
    RequiresMutualTls = true,
    Isolates = false)]
public sealed class Application : INetworkZone
{
}

[ZonePolicy(
    AllowedInbound = new[] { typeof(Application), typeof(Internal) },
    AllowedOutbound = new[] { typeof(Application), typeof(Internal), typeof(Data) },
    RequiresMutualTls = true,
    Isolates = false)]
public sealed class Internal : INetworkZone
{
}

[ZonePolicy(
    AllowedInbound = new Type[] { },
    AllowedOutbound = new[] { typeof(Web), typeof(Application) },
    RequiresMutualTls = false,
    Isolates = false)]
public sealed class External : INetworkZone
{
}

[ZonePolicy(
    AllowedInbound = new[] { typeof(External) },
    AllowedOutbound = new[] { typeof(Application) },
    RequiresMutualTls = true,
    Isolates = false)]
public sealed class Web : INetworkZone
{
}

[ZonePolicy(
    AllowedInbound = new[] { typeof(Application), typeof(Management) },
    AllowedOutbound = new Type[] { },
    RequiresMutualTls = true,
    Isolates = true)]
public sealed class Security : INetworkZone
{
}

[ZonePolicy(
    AllowedInbound = new[] { typeof(Application) },
    AllowedOutbound = new Type[] { },
    RequiresMutualTls = true,
    Isolates = true)]
public sealed class Data : INetworkZone
{
}

[ZonePolicy(
    AllowedInbound = new[] { typeof(Application), typeof(Management), typeof(External) },
    AllowedOutbound = new[] { typeof(Application), typeof(Data), typeof(Security) },
    RequiresMutualTls = true,
    Isolates = false)]
public sealed class Management : INetworkZone
{
}
