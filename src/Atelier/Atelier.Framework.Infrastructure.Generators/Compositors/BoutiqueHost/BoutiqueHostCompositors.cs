using System.Collections.Generic;
using Templar.Rendering;

namespace Atelier.Framework.Infrastructure.Generators.Compositors.BoutiqueHost;

public sealed class EntryPoint : Compositor
{
    public string NamespaceName { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public IEnumerable<Compositor> Products { get; set; } = new List<Compositor>();
    public IEnumerable<Compositor> Registrations { get; set; } = new List<Compositor>();
    public IEnumerable<Compositor> ApiEndpoints { get; set; } = new List<Compositor>();
    public IEnumerable<Compositor> Artifacts { get; set; } = new List<Compositor>();
}

public sealed class ArtifactEntry : Compositor
{
    public string FileName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public sealed class NetworkPolicy : Compositor
{
    public string Name { get; set; } = string.Empty;
    public string Zone { get; set; } = string.Empty;
    public string Mtls { get; set; } = string.Empty;
    public string Ingress { get; set; } = string.Empty;
    public string Egress { get; set; } = string.Empty;
}

public sealed class ZonePeer : Compositor
{
    public string Zone { get; set; } = string.Empty;
}

public sealed class ServiceWithInterface : Compositor
{
    public string Lifetime { get; set; } = string.Empty;
    public string ServiceType { get; set; } = string.Empty;
    public string Implementation { get; set; } = string.Empty;
}

public sealed class ServiceImplOnly : Compositor
{
    public string Lifetime { get; set; } = string.Empty;
    public string Implementation { get; set; } = string.Empty;
}

public sealed class HostedService : Compositor
{
    public string ServiceType { get; set; } = string.Empty;
    public string Implementation { get; set; } = string.Empty;
}

public sealed class ProductEntry : Compositor
{
    public string ProductTypeName { get; set; } = string.Empty;
    public string ProductType { get; set; } = string.Empty;
    public string AutoStart { get; set; } = string.Empty;
}

public sealed class ApiEndpointMap : Compositor
{
    public string EndpointsType { get; set; } = string.Empty;
    public string MapMethod { get; set; } = string.Empty;
}
