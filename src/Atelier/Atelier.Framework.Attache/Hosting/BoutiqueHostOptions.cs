namespace Atelier.Framework.Attache.Hosting;

/// <summary>
/// Deployment configuration for a boutique host, bound from the
/// <c>Atelier:Boutique</c> configuration section (appsettings, environment, args).
/// Listen endpoints use ASP.NET Core's own <c>Kestrel</c> section; when it declares
/// no endpoints the host listens on <c>HTTP_PORT</c>, <c>GRPC_PORT</c> and
/// <c>METRICS_PORT</c>.
/// </summary>
public sealed class BoutiqueHostOptions
{
    public const string SECTION = "Atelier:Boutique";

    public BoutiqueHostCapabilityOptions Capabilities { get; set; } = new();
    public string RestBasePath { get; set; } = "/";
    public string LivenessPath { get; set; } = "/health";
    public string ReadinessPath { get; set; } = "/health/ready";
    public bool NetworkEnabled { get; set; }
}

public sealed class BoutiqueHostCapabilityOptions
{
    public bool Rest { get; set; } = true;
    public bool Grpc { get; set; } = true;
    public bool WebSocket { get; set; } = true;
    public bool GraphQL { get; set; }
}
