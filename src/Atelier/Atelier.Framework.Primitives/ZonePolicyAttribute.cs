namespace Atelier.Framework.Primitives;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ZonePolicyAttribute : Attribute
{
    public Type[] AllowedInbound { get; set; } = [];
    public Type[] AllowedOutbound { get; set; } = [];
    public bool RequiresMutualTls { get; set; }
    public bool Isolates { get; set; }
}
