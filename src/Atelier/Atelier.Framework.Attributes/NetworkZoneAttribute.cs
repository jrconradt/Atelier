namespace Atelier.Framework.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class NetworkZoneAttribute : Attribute
{
    public Type Zone { get; }

    public NetworkZoneAttribute(Type zone)
    {
        Zone = zone;
    }
}
