using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Templar.Rendering;
using BH = Atelier.Framework.Infrastructure.Generators.Compositors.BoutiqueHost;

namespace Atelier.Framework.Infrastructure.Generators;

/// <summary>
/// Emits a boutique host's entry point from <c>[assembly: Boutique]</c>. Everything the
/// host needs from code — DI registrations for <c>[Infrastructure]</c> types, the
/// <c>[Api]</c> endpoint maps, the product manifest — is discovered here from the
/// compilation and its referenced assemblies, and handed to <c>BoutiqueHost.RunAsync</c>
/// as flat lists.
/// </summary>
[Generator]
public sealed class BoutiqueHostSourceGenerator : IIncrementalGenerator
{
    private const string BOUTIQUE_ATTRIBUTE = "Atelier.Framework.Attributes.BoutiqueAttribute";
    private const string BOUTIQUE_PRODUCT_ATTRIBUTE = "Atelier.Framework.Attributes.BoutiqueProductAttribute";
    private const string INFRASTRUCTURE_ATTRIBUTE = "Atelier.Framework.Attributes.InfrastructureAttribute";
    private const string API_ATTRIBUTE = "Atelier.Framework.Attributes.ApiAttribute";
    private const string NETWORK_ZONE_ATTRIBUTE = "Atelier.Framework.Attributes.NetworkZoneAttribute";
    private const string ZONE_POLICY_ATTRIBUTE = "Atelier.Framework.Primitives.ZonePolicyAttribute";
    private const string PRODUCT_BASE = "Atelier.Framework.Offering.Product.ProductBase";
    private const string HOSTED_SERVICE = "Microsoft.Extensions.Hosting.IHostedService";
    private const string ATELIER_ASSEMBLY_PREFIX = "Atelier.";

    private static readonly string[] CORE_ASSEMBLIES =
    {
        "Atelier.Framework.Infrastructure",
        "Atelier.Framework.Context",
        "Atelier.Framework.Observability",
        "Atelier.Framework.Outcomes",
        "Atelier.Framework.Requisitions",
        "Atelier.Framework.Offering",
        "Atelier.Framework.Attache",
        "Atelier.Framework.Facility",
    };

    private static readonly string[] LIFETIME_METHODS = { "AddSingleton", "AddScoped", "AddTransient" };

    private static readonly DiagnosticDescriptor ProductNotProductBaseRule = new DiagnosticDescriptor(
        "ATELIER0900",
        "Boutique product must derive from ProductBase",
        "[BoutiqueProduct] type '{0}' does not derive from Atelier.Framework.Offering.Product.ProductBase",
        "Generator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The boutique host starts each declared product through ProductBase. A type that does not derive from it cannot be started; declare a ProductBase-derived type.",
        customTags: new[] { WellKnownDiagnosticTags.Compiler });

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var models = context.CompilationProvider
            .Select(static (compilation, ct) => BuildModel(compilation, ct));

        context.RegisterSourceOutput(
            models,
            static (spc, model) =>
            {
                if (model is null)
                {
                    return;
                }

                foreach (var diagnostic in model.Diagnostics.Items)
                {
                    spc.ReportDiagnostic(diagnostic.ToDiagnostic());
                }

                spc.AddSource("AtelierBoutiqueHost.g.cs", SourceText.From(RenderSource(model), Encoding.UTF8));
            });
    }

    private static BoutiqueHostModel? BuildModel(Compilation compilation, System.Threading.CancellationToken ct)
    {
        var boutique = compilation.Assembly.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == BOUTIQUE_ATTRIBUTE);
        if (boutique is null)
        {
            return null;
        }

        var id = boutique.ConstructorArguments.Length > 0 ? boutique.ConstructorArguments[0].Value as string ?? string.Empty : string.Empty;
        var name = NamedString(boutique, "Name") ?? id;
        var description = NamedString(boutique, "Description");
        var version = NamedString(boutique, "Version") ?? "1.0.0";

        var diagnostics = new List<BoutiqueHostDiagnostic>();
        var products = new List<ProductModel>();
        var productTypes = new List<INamedTypeSymbol>();

        foreach (var attribute in compilation.Assembly.GetAttributes()
                     .Where(a => a.AttributeClass?.ToDisplayString() == BOUTIQUE_PRODUCT_ATTRIBUTE))
        {
            if (attribute.ConstructorArguments.Length == 0
                || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol productType)
            {
                continue;
            }

            if (!DerivesFrom(productType, PRODUCT_BASE))
            {
                diagnostics.Add(BoutiqueHostDiagnostic.At(ProductNotProductBaseRule, attribute, productType.ToDisplayString()));
                continue;
            }

            var autoStart = attribute.NamedArguments
                .Where(kvp => kvp.Key == "AutoStart")
                .Select(kvp => kvp.Value.Value as bool?)
                .FirstOrDefault() ?? true;

            products.Add(new ProductModel(
                productType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                productType.ToDisplayString(),
                autoStart));
            productTypes.Add(productType);
        }

        var productClosure = CollectProductClosure(productTypes.Select(t => t.ContainingAssembly));
        var participating = CollectParticipatingAssemblies(compilation, productClosure);

        var registrations = new List<RegistrationModel>();
        var apiEndpoints = new List<ApiEndpointModel>();
        foreach (var assembly in participating)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var type in EnumerateTopLevelTypes(assembly.GlobalNamespace))
            {
                var registration = TryBuildRegistration(type);
                if (registration is not null)
                {
                    registrations.Add(registration);
                }

                if (!SymbolEqualityComparer.Default.Equals(assembly, compilation.Assembly))
                {
                    var endpoints = TryBuildApiEndpoints(assembly, type);
                    if (endpoints is not null)
                    {
                        apiEndpoints.Add(endpoints);
                    }
                }
            }
        }

        var zonePolicies = CollectZonePolicies(productClosure, productTypes);
        var requisiteAssemblies = CollectRequisiteAssemblies(compilation);
        var artifacts = RenderArtifacts(id, name, version, products, requisiteAssemblies, zonePolicies);

        return new BoutiqueHostModel(
            ToNamespace(compilation.AssemblyName),
            id,
            name,
            description,
            version,
            new EquatableArray<ProductModel>(products.ToImmutableArray()),
            new EquatableArray<ArtifactModel>(artifacts.ToImmutableArray()),
            new EquatableArray<RegistrationModel>(registrations
                .GroupBy(r => r.ServiceType + "|" + r.Implementation, StringComparer.Ordinal)
                .Select(g => g.First())
                .OrderBy(r => r.Implementation, StringComparer.Ordinal)
                .ToImmutableArray()),
            new EquatableArray<ApiEndpointModel>(apiEndpoints
                .OrderBy(e => e.EndpointsType, StringComparer.Ordinal)
                .ToImmutableArray()),
            new EquatableArray<BoutiqueHostDiagnostic>(diagnostics.ToImmutableArray()));
    }

    private static List<IAssemblySymbol> CollectParticipatingAssemblies(
        Compilation compilation,
        IEnumerable<IAssemblySymbol> productClosure)
    {
        var referenced = compilation.SourceModule.ReferencedAssemblySymbols
            .GroupBy(a => a.Identity.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var participating = new Dictionary<string, IAssemblySymbol>(StringComparer.Ordinal)
        {
            [compilation.Assembly.Identity.Name] = compilation.Assembly,
        };

        foreach (var core in CORE_ASSEMBLIES)
        {
            if (referenced.TryGetValue(core, out var assembly))
            {
                participating[core] = assembly;
            }
        }

        foreach (var assembly in productClosure)
        {
            participating[assembly.Identity.Name] = assembly;
        }

        return participating
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .Select(kvp => kvp.Value)
            .ToList();
    }

    // A product brings in every Atelier assembly reachable from its own, the same
    // closure smash's ProductDependencyAnalyzer used to compute over compiled DLLs.
    private static List<IAssemblySymbol> CollectProductClosure(IEnumerable<IAssemblySymbol> productAssemblies)
    {
        var closure = new Dictionary<string, IAssemblySymbol>(StringComparer.Ordinal);
        var queue = new Queue<IAssemblySymbol>(productAssemblies);
        while (queue.Count > 0)
        {
            var assembly = queue.Dequeue();
            if (closure.ContainsKey(assembly.Identity.Name))
            {
                continue;
            }

            closure[assembly.Identity.Name] = assembly;

            foreach (var dependency in assembly.Modules.SelectMany(m => m.ReferencedAssemblySymbols))
            {
                if (dependency.Identity.Name.StartsWith(ATELIER_ASSEMBLY_PREFIX, StringComparison.Ordinal))
                {
                    queue.Enqueue(dependency);
                }
            }
        }

        return closure
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .Select(kvp => kvp.Value)
            .ToList();
    }

    private static List<string> CollectRequisiteAssemblies(Compilation compilation)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal) { compilation.Assembly.Identity.Name };
        var queue = new Queue<IAssemblySymbol>(compilation.SourceModule.ReferencedAssemblySymbols);
        while (queue.Count > 0)
        {
            var assembly = queue.Dequeue();
            if (!assembly.Identity.Name.StartsWith(ATELIER_ASSEMBLY_PREFIX, StringComparison.Ordinal)
                || !names.Add(assembly.Identity.Name))
            {
                continue;
            }

            foreach (var dependency in assembly.Modules.SelectMany(m => m.ReferencedAssemblySymbols))
            {
                queue.Enqueue(dependency);
            }
        }

        return names.ToList();
    }

    private static List<ZonePolicyModel> CollectZonePolicies(
        IEnumerable<IAssemblySymbol> productClosure,
        IReadOnlyCollection<INamedTypeSymbol> productTypes)
    {
        var zoned = productClosure
            .SelectMany(assembly => EnumerateTopLevelTypes(assembly.GlobalNamespace))
            .Where(type => type.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == INFRASTRUCTURE_ATTRIBUTE))
            .Concat(productTypes);

        var policies = new SortedDictionary<string, ZonePolicyModel>(StringComparer.Ordinal);
        foreach (var type in zoned)
        {
            foreach (var zoneAttribute in type.GetAttributes()
                         .Where(a => a.AttributeClass?.ToDisplayString() == NETWORK_ZONE_ATTRIBUTE))
            {
                if (zoneAttribute.ConstructorArguments.Length == 0
                    || zoneAttribute.ConstructorArguments[0].Value is not INamedTypeSymbol zone)
                {
                    continue;
                }

                var policy = ReadZonePolicy(zone);
                if (policy is not null)
                {
                    policies[policy.Zone] = policy;
                }
            }
        }

        return policies.Values.ToList();
    }

    private static ZonePolicyModel? ReadZonePolicy(INamedTypeSymbol zone)
    {
        var attribute = zone.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ZONE_POLICY_ATTRIBUTE);
        if (attribute is null)
        {
            return null;
        }

        var inbound = ImmutableArray<string>.Empty;
        var outbound = ImmutableArray<string>.Empty;
        var requiresMutualTls = false;
        var isolates = false;
        foreach (var named in attribute.NamedArguments)
        {
            switch (named.Key)
            {
                case "AllowedInbound":
                    inbound = ZoneNames(named.Value);
                    break;
                case "AllowedOutbound":
                    outbound = ZoneNames(named.Value);
                    break;
                case "RequiresMutualTls":
                    requiresMutualTls = named.Value.Value is true;
                    break;
                case "Isolates":
                    isolates = named.Value.Value is true;
                    break;
            }
        }

        return new ZonePolicyModel(
            zone.Name.ToLowerInvariant(),
            new EquatableArray<string>(inbound),
            new EquatableArray<string>(outbound),
            requiresMutualTls,
            isolates);
    }

    private static ImmutableArray<string> ZoneNames(TypedConstant value)
    {
        return value.Kind == TypedConstantKind.Array
            ? value.Values
                .Select(v => v.Value)
                .OfType<INamedTypeSymbol>()
                .Select(zone => zone.Name.ToLowerInvariant())
                .ToImmutableArray()
            : ImmutableArray<string>.Empty;
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTopLevelTypes(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            yield return type;
        }

        foreach (var child in ns.GetNamespaceMembers())
        {
            foreach (var type in EnumerateTopLevelTypes(child))
            {
                yield return type;
            }
        }
    }

    private static RegistrationModel? TryBuildRegistration(INamedTypeSymbol type)
    {
        if (type.TypeKind != TypeKind.Class
            || type.DeclaredAccessibility != Accessibility.Public
            || type.IsAbstract
            || type.IsStatic
            || type.IsGenericType)
        {
            return null;
        }

        var attribute = type.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == INFRASTRUCTURE_ATTRIBUTE);
        if (attribute is null)
        {
            return null;
        }

        var args = attribute.ConstructorArguments;
        INamedTypeSymbol? explicitServiceType = null;
        int lifetimeValue;
        if (args.Length == 1)
        {
            lifetimeValue = ToInt(args[0].Value);
        }
        else if (args.Length >= 3)
        {
            explicitServiceType = args[0].Value as INamedTypeSymbol;
            lifetimeValue = ToInt(args[2].Value);
        }
        else
        {
            return null;
        }

        var serviceType = explicitServiceType
            ?? type.AllInterfaces.FirstOrDefault(i => i.Name == "I" + type.Name);
        if (serviceType is not null && !IsNameableFromHost(serviceType))
        {
            serviceType = null;
        }

        var implementation = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var service = serviceType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? implementation;
        var lifetime = LIFETIME_METHODS[Math.Max(0, Math.Min(lifetimeValue, LIFETIME_METHODS.Length - 1))];
        var isHostedService = lifetimeValue == 0
            && type.AllInterfaces.Any(i => i.ToDisplayString() == HOSTED_SERVICE);

        return new RegistrationModel(
            lifetime,
            service,
            implementation,
            !string.Equals(service, implementation, StringComparison.Ordinal),
            isHostedService);
    }

    private static ApiEndpointModel? TryBuildApiEndpoints(IAssemblySymbol assembly, INamedTypeSymbol type)
    {
        if (!type.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == API_ATTRIBUTE))
        {
            return null;
        }

        var endpointsName = type.ContainingNamespace.IsGlobalNamespace
            ? type.Name + "ApiEndpoints"
            : type.ContainingNamespace.ToDisplayString() + "." + type.Name + "ApiEndpoints";
        var endpoints = assembly.GetTypeByMetadataName(endpointsName);
        var mapMethod = "Map" + type.Name + "Endpoints";
        if (endpoints is null
            || endpoints.DeclaredAccessibility != Accessibility.Public
            || !endpoints.GetMembers(mapMethod).OfType<IMethodSymbol>().Any(m => m.IsStatic && m.DeclaredAccessibility == Accessibility.Public))
        {
            return null;
        }

        return new ApiEndpointModel(endpoints.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), mapMethod);
    }

    private static bool IsNameableFromHost(INamedTypeSymbol type)
    {
        return type.DeclaredAccessibility == Accessibility.Public
            && type.ContainingType is null
            && !type.IsGenericType;
    }

    private static bool DerivesFrom(INamedTypeSymbol type, string baseTypeName)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == baseTypeName)
            {
                return true;
            }
        }

        return false;
    }

    private static string? NamedString(AttributeData attribute, string name)
    {
        return attribute.NamedArguments
            .Where(kvp => kvp.Key == name)
            .Select(kvp => kvp.Value.Value as string)
            .FirstOrDefault();
    }

    private static int ToInt(object? value)
    {
        return value is null ? 0 : Convert.ToInt32(value);
    }

    private static string ToNamespace(string? assemblyName)
    {
        var parts = (assemblyName ?? "AtelierHost")
            .Split('.')
            .Select(part => new string(part.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray()))
            .Select(part => part.Length == 0 || char.IsDigit(part[0]) ? "_" + part : part);
        return string.Join(".", parts);
    }

    private static string Literal(string? value)
    {
        return value is null ? "null" : SymbolDisplay.FormatLiteral(value, quote: true);
    }

    private static List<ArtifactModel> RenderArtifacts(
        string id,
        string name,
        string version,
        IReadOnlyList<ProductModel> products,
        IReadOnlyList<string> requisiteAssemblies,
        IReadOnlyList<ZonePolicyModel> zonePolicies)
    {
        var artifacts = new List<ArtifactModel>
        {
            new ArtifactModel("requisite-manifest.json", RenderManifest(id, name, version, products, requisiteAssemblies)),
            new ArtifactModel("network-zones.json", RenderNetworkZones(zonePolicies)),
        };

        if (zonePolicies.Count > 0)
        {
            artifacts.Add(new ArtifactModel("network-policies.yaml", string.Join("---\n", zonePolicies.Select(RenderPolicy))));
        }

        return artifacts;
    }

    private static string RenderManifest(
        string id,
        string name,
        string version,
        IReadOnlyList<ProductModel> products,
        IReadOnlyList<string> requisiteAssemblies)
    {
        var json = new StringBuilder();
        json.Append("{\n");
        json.Append("  \"boutique\": ").Append(JsonString(id)).Append(",\n");
        json.Append("  \"name\": ").Append(JsonString(name)).Append(",\n");
        json.Append("  \"version\": ").Append(JsonString(version)).Append(",\n");
        json.Append("  \"products\": ").Append(JsonArray(products.Select(p => p.DisplayName))).Append(",\n");
        json.Append("  \"requisiteAssemblies\": ").Append(JsonArray(requisiteAssemblies)).Append("\n");
        json.Append("}\n");
        return json.ToString();
    }

    private static string RenderNetworkZones(IReadOnlyList<ZonePolicyModel> zonePolicies)
    {
        var isolated = zonePolicies.Where(p => p.Isolates).Select(p => p.Zone);
        return "{\n  \"isolatedNetworks\": " + JsonArray(isolated) + "\n}\n";
    }

    private static string RenderPolicy(ZonePolicyModel policy)
    {
        var yaml = new BH.NetworkPolicy
        {
            Name = policy.Zone + "-zone-policy",
            Zone = policy.Zone,
            Mtls = policy.RequiresMutualTls ? "true" : "false",
            Ingress = RenderPeers("    - from:", policy.AllowedInbound.Items),
            Egress = RenderPeers("    - to:", policy.AllowedOutbound.Items),
        }.Render();

        var lines = yaml
            .Split('\n')
            .Where(line => line.Trim().Length > 0);

        return string.Join("\n", lines) + "\n";
    }

    private static string RenderPeers(string ruleLine, ImmutableArray<string> zones)
    {
        if (zones.Length == 0)
        {
            return "    []";
        }

        var peers = zones.Select(zone => new BH.ZonePeer { Zone = zone }.Render().TrimEnd('\n'));
        return ruleLine + "\n" + string.Join("\n", peers);
    }

    private static string JsonArray(IEnumerable<string> values)
    {
        return "[" + string.Join(", ", values.Select(JsonString)) + "]";
    }

    private static string JsonString(string value)
    {
        var escaped = new StringBuilder("\"");
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': escaped.Append("\\\""); break;
                case '\\': escaped.Append("\\\\"); break;
                case '\n': escaped.Append("\\n"); break;
                case '\r': escaped.Append("\\r"); break;
                case '\t': escaped.Append("\\t"); break;
                default:
                    if (c < ' ')
                    {
                        escaped.Append("\\u").Append(((int)c).ToString("x4"));
                    }
                    else
                    {
                        escaped.Append(c);
                    }
                    break;
            }
        }

        return escaped.Append('"').ToString();
    }

    private static string RenderSource(BoutiqueHostModel model)
    {
        return new BH.EntryPoint
        {
            NamespaceName = model.NamespaceName,
            Id = Literal(model.Id),
            Name = Literal(model.Name),
            Description = Literal(model.Description),
            Version = Literal(model.Version),
            Products = model.Products.Items
                .Select(p => (Compositor)new BH.ProductEntry
                {
                    ProductTypeName = Literal(p.DisplayName),
                    ProductType = p.TypeName,
                    AutoStart = p.AutoStart ? "true" : "false",
                })
                .ToList(),
            Registrations = model.Registrations.Items
                .SelectMany(RenderRegistration)
                .ToList(),
            Artifacts = model.Artifacts.Items
                .Select(a => (Compositor)new BH.ArtifactEntry
                {
                    FileName = Literal(a.FileName),
                    Content = Literal(a.Content),
                })
                .ToList(),
            ApiEndpoints = model.ApiEndpoints.Items
                .Select(e => (Compositor)new BH.ApiEndpointMap
                {
                    EndpointsType = e.EndpointsType,
                    MapMethod = e.MapMethod,
                })
                .ToList(),
        }.Render();
    }

    private static IEnumerable<Compositor> RenderRegistration(RegistrationModel registration)
    {
        if (registration.HasInterface)
        {
            yield return new BH.ServiceWithInterface
            {
                Lifetime = registration.Lifetime,
                ServiceType = registration.ServiceType,
                Implementation = registration.Implementation,
            };
        }
        else
        {
            yield return new BH.ServiceImplOnly
            {
                Lifetime = registration.Lifetime,
                Implementation = registration.Implementation,
            };
        }

        if (registration.IsHostedService)
        {
            yield return new BH.HostedService
            {
                ServiceType = registration.ServiceType,
                Implementation = registration.Implementation,
            };
        }
    }
}

internal sealed record BoutiqueHostModel(
    string NamespaceName,
    string Id,
    string Name,
    string? Description,
    string Version,
    EquatableArray<ProductModel> Products,
    EquatableArray<ArtifactModel> Artifacts,
    EquatableArray<RegistrationModel> Registrations,
    EquatableArray<ApiEndpointModel> ApiEndpoints,
    EquatableArray<BoutiqueHostDiagnostic> Diagnostics);

internal sealed record ProductModel(string TypeName, string DisplayName, bool AutoStart);

internal sealed record RegistrationModel(
    string Lifetime,
    string ServiceType,
    string Implementation,
    bool HasInterface,
    bool IsHostedService);

internal sealed record ApiEndpointModel(string EndpointsType, string MapMethod);

internal sealed record ArtifactModel(string FileName, string Content);

internal sealed record ZonePolicyModel(
    string Zone,
    EquatableArray<string> AllowedInbound,
    EquatableArray<string> AllowedOutbound,
    bool RequiresMutualTls,
    bool Isolates);

internal sealed record BoutiqueHostDiagnostic(
    DiagnosticDescriptor Descriptor,
    string? FilePath,
    TextSpan TextSpan,
    LinePositionSpan LineSpan,
    string MessageArg)
{
    public static BoutiqueHostDiagnostic At(DiagnosticDescriptor descriptor, AttributeData attribute, string messageArg)
    {
        var location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation();
        if (location is null || !location.IsInSource)
        {
            return new BoutiqueHostDiagnostic(descriptor, null, default, default, messageArg);
        }

        return new BoutiqueHostDiagnostic(
            descriptor,
            location.SourceTree?.FilePath,
            location.SourceSpan,
            location.GetLineSpan().Span,
            messageArg);
    }

    public Diagnostic ToDiagnostic()
    {
        var location = FilePath is null
            ? Location.None
            : Location.Create(FilePath, TextSpan, LineSpan);
        return Diagnostic.Create(Descriptor, location, MessageArg);
    }
}

internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>
    where T : IEquatable<T>
{
    private readonly ImmutableArray<T> _items;

    public EquatableArray(ImmutableArray<T> items)
    {
        _items = items;
    }

    public ImmutableArray<T> Items => _items.IsDefault ? ImmutableArray<T>.Empty : _items;

    public bool Equals(EquatableArray<T> other)
    {
        return Items.SequenceEqual(other.Items);
    }

    public override bool Equals(object? obj)
    {
        return obj is EquatableArray<T> other && Equals(other);
    }

    public override int GetHashCode()
    {
        var hash = 17;
        foreach (var item in Items)
        {
            hash = unchecked((hash * 31) + (item?.GetHashCode() ?? 0));
        }

        return hash;
    }
}
