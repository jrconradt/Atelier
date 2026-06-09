using Atelier.Framework.Network;
namespace Atelier.Framework.Network.Attributes;

[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false)]
public class HttpTransportAttribute : Attribute
{
    public string BasePath { get; set; } = "/api";
    public int Port { get; set; } = 80;
    public bool EnableCors { get; set; } = false;
}

[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false)]
public class InProcessTransportAttribute : Attribute
{
    public bool EnablePooling { get; set; } = false;
    public int MaxPoolSize { get; set; } = 100;
}
