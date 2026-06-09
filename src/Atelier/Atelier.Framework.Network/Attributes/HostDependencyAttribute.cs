using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Atelier.Framework.Network.Attributes
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public class HostDependencyAttribute : Attribute
    {
        public DependencyType Type { get; }
        public Type ServiceType { get; }
        public string[]? AllowedNetworkPaths { get; }
        public bool RequiresEncryption { get; }
        public TimeSpan? MaxLatency { get; }

        public HostDependencyAttribute(
            Type serviceType,
            DependencyType type = DependencyType.Required,
            string[]? allowedNetworkPaths = null,
            bool requiresEncryption = true,
            int maxLatencyMilliseconds = 0)
        {
            ArgumentNullException.ThrowIfNull(serviceType);
            ServiceType = serviceType;
            Type = type;
            AllowedNetworkPaths = allowedNetworkPaths;
            RequiresEncryption = requiresEncryption;
            MaxLatency = maxLatencyMilliseconds > 0
                ? TimeSpan.FromMilliseconds(maxLatencyMilliseconds)
                : null;
        }
    }

    public enum DependencyType
    {
        Required = 0,
        Optional = 1,
        Fallback = 2
    }
}
