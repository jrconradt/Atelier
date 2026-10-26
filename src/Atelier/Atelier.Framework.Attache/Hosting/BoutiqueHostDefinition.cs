using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Atelier.Framework.Attache.Hosting;

/// <summary>
/// The compile-time description of a boutique host. Emitted by the boutique host
/// source generator from <c>[assembly: Boutique]</c> and the assemblies the host
/// references; <see cref="BoutiqueHost"/> runs it.
/// </summary>
public sealed class BoutiqueHostDefinition
{
    public BoutiqueHostDefinition(
        string id,
        string name,
        string? description,
        string version,
        IReadOnlyList<ProductManifest> products,
        IReadOnlyList<BoutiqueArtifact> artifacts,
        Action<IServiceCollection> registerServices,
        Action<IEndpointRouteBuilder> mapApiEndpoints,
        Action<IServiceCollection> configureServices,
        Action<WebApplication> configureApplication)
    {
        Id = id;
        Name = name;
        Description = description;
        Version = version;
        Products = products;
        Artifacts = artifacts;
        RegisterServices = registerServices;
        MapApiEndpoints = mapApiEndpoints;
        ConfigureServices = configureServices;
        ConfigureApplication = configureApplication;
    }

    internal string Id { get; }
    internal string Name { get; }
    internal string? Description { get; }
    internal string Version { get; }
    internal IReadOnlyList<ProductManifest> Products { get; }
    internal IReadOnlyList<BoutiqueArtifact> Artifacts { get; }
    internal Action<IServiceCollection> RegisterServices { get; }
    internal Action<IEndpointRouteBuilder> MapApiEndpoints { get; }
    internal Action<IServiceCollection> ConfigureServices { get; }
    internal Action<WebApplication> ConfigureApplication { get; }
}
