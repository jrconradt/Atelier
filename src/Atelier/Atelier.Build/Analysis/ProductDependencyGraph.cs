using Templar.Rendering;
using T = Atelier.Build.Templates.Diagram;

namespace Atelier.Build.Analysis;

public class ProductDependencyGraph
{
    private readonly HashSet<string> _requiredAssemblies = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ProductInfo> _products = new();
    private readonly Dictionary<string, List<DependencyEdge>> _dependencies = new();
    private readonly HashSet<string> _visitedTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _isolatedNetworks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ZonePolicyInfo> _zonePolicies = new(StringComparer.Ordinal);

    private static readonly HashSet<string> CoreInfrastructureAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "Atelier.Framework.Infrastructure",
        "Atelier.Framework.Context",
        "Atelier.Framework.Observability",
        "Atelier.Framework.Outcomes",
        "Atelier.Framework.Requisitions",
        "Atelier.Framework.Offering",
        "Atelier.Framework.Attache",
        "Atelier.Framework.Facility"
    };

    public IReadOnlySet<string> RequiredAssemblies => _requiredAssemblies;
    public IReadOnlyList<ProductInfo> Products => _products;
    public IReadOnlyDictionary<string, List<DependencyEdge>> Dependencies => _dependencies;
    public IReadOnlySet<string> VisitedTypes => _visitedTypes;
    public IReadOnlySet<string> IsolatedNetworks => _isolatedNetworks;
    public IReadOnlyCollection<ZonePolicyInfo> ZonePolicies => _zonePolicies.Values;

    public void AddAssembly(string assemblyName)
    {
        _requiredAssemblies.Add(assemblyName);
    }

    public void AddProduct(ProductInfo product)
    {
        _products.Add(product);
    }

    public void AddVisitedType(string typeFullName)
    {
        _visitedTypes.Add(typeFullName);
    }

    public bool HasVisitedType(string typeFullName)
    {
        return _visitedTypes.Contains(typeFullName);
    }

    public void AddIsolatedNetwork(string zone)
    {
        _isolatedNetworks.Add(zone);
    }

    public void AddZonePolicy(ZonePolicyInfo policy)
    {
        _zonePolicies[policy.Zone] = policy;

        if (policy.Isolates)
        {
            _isolatedNetworks.Add(policy.Zone);
        }
    }

    public void AddDependency(string fromType, string toType, string edgeType)
    {
        if (!_dependencies.ContainsKey(fromType))
        {
            _dependencies[fromType] = new List<DependencyEdge>();
        }

        _dependencies[fromType].Add(new DependencyEdge
        {
            FromType = fromType,
            ToType = toType,
            EdgeType = edgeType
        });
    }

    public IEnumerable<string> GetBoutiqueSpecificAssemblies()
    {
        return _requiredAssemblies
            .Except(CoreInfrastructureAssemblies, StringComparer.OrdinalIgnoreCase)
            .OrderBy(a => a);
    }

    public IEnumerable<string> GetCoreAssemblies()
    {
        return _requiredAssemblies
            .Intersect(CoreInfrastructureAssemblies, StringComparer.OrdinalIgnoreCase)
            .OrderBy(a => a);
    }

    public IEnumerable<string> GetAllAssemblies()
    {
        return _requiredAssemblies.OrderBy(a => a);
    }

    public int TotalAssemblyCount => _requiredAssemblies.Count;
    public int BoutiqueSpecificCount => GetBoutiqueSpecificAssemblies().Count();
    public int CoreCount => GetCoreAssemblies().Count();
    public int TypeCount => _visitedTypes.Count;

    public void Merge(ProductDependencyGraph other)
    {
        foreach (var assembly in other.RequiredAssemblies)
        {
            _requiredAssemblies.Add(assembly);
        }

        foreach (var product in other.Products)
        {
            if (!_products.Any(p => p.TypeName == product.TypeName))
            {
                _products.Add(product);
            }
        }

        foreach (var type in other.VisitedTypes)
        {
            _visitedTypes.Add(type);
        }

        foreach (var network in other.IsolatedNetworks)
        {
            _isolatedNetworks.Add(network);
        }

        foreach (var policy in other.ZonePolicies)
        {
            _zonePolicies[policy.Zone] = policy;
        }

        foreach (var (type, edges) in other.Dependencies)
        {
            if (!_dependencies.ContainsKey(type))
            {
                _dependencies[type] = new List<DependencyEdge>();
            }

            _dependencies[type].AddRange(edges);
        }
    }

    public string ToMermaid()
    {
        var nodes = Sequence.Lines(_products.Select(p => (Compositor)new T.ProductNode
            {
                Id = SanitizeMermaidId(p.TypeName),
                Name = p.TypeName,
            }));

        var edges = Sequence.Lines(_dependencies.SelectMany(kv => kv.Value.Select(edge => (Compositor)new T.ProductEdge
            {
                From = SanitizeMermaidId(kv.Key),
                Label = edge.EdgeType switch
                {
                    "Requisite" => "--|Requisite|-->",
                    "BaseType" => "--|inherits|-->",
                    "Interface" => "-.->",
                    _ => "-->"
                },
                To = SanitizeMermaidId(edge.ToType),
            })));

        return new T.ProductGraphMermaid { Nodes = nodes, Edges = edges }.Render();
    }

    private static string SanitizeMermaidId(string id)
    {
        return id.Replace(".", "_").Replace("<", "_").Replace(">", "_").Replace(",", "_");
    }
}

public class ProductInfo
{
    public required string TypeName { get; init; }
    public required string AssemblyName { get; init; }
    public string? AssemblyPath { get; init; }
    public bool AutoStart { get; init; } = true;
    public Dictionary<string, object>? Configuration { get; init; }
}

public class DependencyEdge
{
    public required string FromType { get; init; }
    public required string ToType { get; init; }
    public required string EdgeType { get; init; }
}

public sealed record ZonePolicyInfo(
    string Zone,
    IReadOnlyList<string> AllowedInbound,
    IReadOnlyList<string> AllowedOutbound,
    bool RequiresMutualTls,
    bool Isolates);
