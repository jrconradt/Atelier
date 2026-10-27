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
    private const string REQUISITE_ATTRIBUTE = "Atelier.Framework.Requisitions.RequisiteAttribute";
    private const string REQUISITE_EDGE_ATTRIBUTE = "Atelier.Framework.Requisitions.RequisiteEdgeAttribute";
    private const string PRODUCT_BASE = "Atelier.Framework.Offering.Product.ProductBase";
    private const string OFFERING_PROVIDER = "Atelier.Framework.Offering.IOfferingProvider";
    private const string SERVICE_PROVIDER_OFFERING_PROVIDER = "Atelier.Framework.Offering.ServiceProviderOfferingProvider";
    private const string NULL_OFFERING_PROVIDER = "Atelier.Framework.Offering.NullOfferingProvider";
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

    // In-process services the .NET host provides. A requisite on one of these is not an
    // edge between boutique services.
    private static readonly HashSet<string> PLATFORM_SERVICES = new(StringComparer.Ordinal)
    {
        "Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService",
    };

    private static readonly DiagnosticDescriptor ProductNotProductBaseRule = new DiagnosticDescriptor(
        "ATELIER0900",
        "Boutique product must derive from ProductBase",
        "[BoutiqueProduct] type '{0}' does not derive from Atelier.Framework.Offering.Product.ProductBase",
        "Generator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The boutique host starts each declared product through ProductBase. A type that does not derive from it cannot be started; declare a ProductBase-derived type.",
        customTags: new[] { WellKnownDiagnosticTags.Compiler });

    private static readonly DiagnosticDescriptor UnprovidedRequisiteRule = new DiagnosticDescriptor(
        "ATELIER0901",
        "Requisite has no providing service",
        "{0}, which no service in this boutique provides",
        "Generator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Every [Requisite] is an edge to the boutique service that provides it. A requisite no service provides has nothing to route to; register a provider with [Infrastructure].",
        customTags: new[] { WellKnownDiagnosticTags.Compiler });

    private static readonly DiagnosticDescriptor AmbiguousRequisiteRule = new DiagnosticDescriptor(
        "ATELIER0902",
        "Requisite has more than one providing service",
        "{0}, which more than one service in this boutique provides",
        "Generator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A single [Requisite] must resolve to exactly one service. Require IEnumerable<T> to take every provider, or narrow the contract.",
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
        var services = new List<INamedTypeSymbol>(productTypes);
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
                    services.Add(type);
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

        AddProductRegistrations(productTypes, registrations);
        AddOfferingProviderRegistration(compilation, productTypes.Count > 0, registrations, services);

        var requisiteAssemblies = CollectRequisiteAssemblies(compilation);
        var serviceEdges = CollectServiceEdges(compilation, services, registrations, boutique, diagnostics);
        var artifacts = RenderArtifacts(id, name, version, products, requisiteAssemblies, serviceEdges);

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

    // Declared products are registered as themselves and as ProductBase, so the boutique
    // receives them as requisites instead of activating them by type at runtime.
    private static void AddProductRegistrations(
        IReadOnlyList<INamedTypeSymbol> productTypes,
        List<RegistrationModel> registrations)
    {
        foreach (var productType in productTypes)
        {
            var implementation = productType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var index = registrations.FindIndex(r => r.Implementation == implementation && !r.HasInterface);
            if (index >= 0)
            {
                registrations[index] = registrations[index] with { IsProduct = true };
            }
            else
            {
                registrations.Add(new RegistrationModel("AddSingleton", implementation, implementation, false, false, IsProduct: true));
            }
        }
    }

    // Which offering provider a boutique uses is known from its declared products.
    private static void AddOfferingProviderRegistration(
        Compilation compilation,
        bool hasProducts,
        List<RegistrationModel> registrations,
        List<INamedTypeSymbol> services)
    {
        var contract = compilation.GetTypeByMetadataName(OFFERING_PROVIDER);
        var provider = compilation.GetTypeByMetadataName(hasProducts ? SERVICE_PROVIDER_OFFERING_PROVIDER : NULL_OFFERING_PROVIDER);
        if (contract is null || provider is null)
        {
            return;
        }

        registrations.Add(new RegistrationModel(
            "AddSingleton",
            contract.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            provider.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            true,
            false));

        if (!services.Contains(provider, SymbolEqualityComparer.Default))
        {
            services.Add(provider);
        }
    }

    // A service's identity is its Namespace.ServiceName. Each [Requisite] member is an
    // edge to the host service that provides the member's declared type; a requisite no
    // host service provides has nothing to route to and is not an edge. Types compiled here are read directly; referenced types' private
    // members are not visible, so their edges come from the [assembly: RequisiteEdge]
    // declarations the requisites generator emitted into their assembly.
    private static List<ServiceEdgesModel> CollectServiceEdges(
        Compilation compilation,
        IReadOnlyList<INamedTypeSymbol> services,
        IReadOnlyList<RegistrationModel> registrations,
        AttributeData boutique,
        List<BoutiqueHostDiagnostic> diagnostics)
    {
        var declared = CollectDeclaredEdges(services
            .SelectMany(TypeChain)
            .Select(type => type.ContainingAssembly)
            .Where(assembly => assembly is not null && !SymbolEqualityComparer.Default.Equals(assembly, compilation.Assembly))
            .Distinct(SymbolEqualityComparer.Default)
            .Cast<IAssemblySymbol>());

        // Providers mirror the registrations DI will hold: each contract maps to every
        // implementation registered for it, and every service provides itself.
        var providers = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        void AddProvider(string contract, string implementation)
        {
            if (!providers.TryGetValue(contract, out var set))
            {
                set = new SortedSet<string>(StringComparer.Ordinal);
                providers[contract] = set;
            }

            set.Add(implementation);
        }

        foreach (var service in services)
        {
            AddProvider(Identity(service), Identity(service));
        }

        foreach (var registration in registrations)
        {
            AddProvider(StripGlobal(registration.ServiceType), StripGlobal(registration.Implementation));
            if (registration.IsProduct)
            {
                AddProvider(PRODUCT_BASE, StripGlobal(registration.Implementation));
            }
        }

        var models = new SortedDictionary<string, ServiceEdgesModel>(StringComparer.Ordinal);
        foreach (var service in services)
        {
            var edges = new SortedSet<string>(StringComparer.Ordinal);
            var identity = Identity(service);
            foreach (var type in TypeChain(service))
            {
                var targets = SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly)
                    ? type.GetMembers().Select(RequisiteType).OfType<ITypeSymbol>()
                    : declared.TryGetValue(Identity(type), out var edgesOfType) ? edgesOfType : Enumerable.Empty<ITypeSymbol>();

                foreach (var target in targets)
                {
                    if (CollectionElement(target) is { } element)
                    {
                        if (providers.TryGetValue(Identity(element), out var all))
                        {
                            edges.UnionWith(all);
                        }

                        continue;
                    }

                    var targetIdentity = Identity(target);
                    providers.TryGetValue(targetIdentity, out var found);
                    switch (found?.Count ?? 0)
                    {
                        case 1:
                            edges.Add(found!.Min!);
                            break;
                        case 0 when PLATFORM_SERVICES.Contains(targetIdentity):
                            break;
                        case 0:
                            diagnostics.Add(BoutiqueHostDiagnostic.At(UnprovidedRequisiteRule, boutique, $"'{identity}' requires '{targetIdentity}'"));
                            break;
                        default:
                            diagnostics.Add(BoutiqueHostDiagnostic.At(AmbiguousRequisiteRule, boutique, $"'{identity}' requires '{targetIdentity}'"));
                            break;
                    }
                }
            }

            models[identity] = new ServiceEdgesModel(
                identity,
                new EquatableArray<string>(edges.ToImmutableArray()));
        }

        return models.Values.ToList();
    }

    private static IEnumerable<INamedTypeSymbol> TypeChain(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            yield return current;
        }
    }

    private static Dictionary<string, List<ITypeSymbol>> CollectDeclaredEdges(IEnumerable<IAssemblySymbol> assemblies)
    {
        var declared = new Dictionary<string, List<ITypeSymbol>>(StringComparer.Ordinal);
        foreach (var attribute in assemblies.SelectMany(a => a.GetAttributes()))
        {
            if (attribute.AttributeClass?.ToDisplayString() != REQUISITE_EDGE_ATTRIBUTE
                || attribute.ConstructorArguments.Length != 2
                || attribute.ConstructorArguments[0].Value is not ITypeSymbol service
                || attribute.ConstructorArguments[1].Value is not ITypeSymbol requisite)
            {
                continue;
            }

            var key = Identity(service);
            if (!declared.TryGetValue(key, out var targets))
            {
                targets = new List<ITypeSymbol>();
                declared[key] = targets;
            }

            targets.Add(requisite);
        }

        return declared;
    }

    private static ITypeSymbol? RequisiteType(ISymbol member)
    {
        if (!member.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == REQUISITE_ATTRIBUTE))
        {
            return null;
        }

        return member switch
        {
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            _ => null,
        };
    }

    private static ITypeSymbol? CollectionElement(ITypeSymbol type)
    {
        return type is INamedTypeSymbol { IsGenericType: true } named
            && named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T
                ? named.TypeArguments[0]
                : null;
    }

    private static string StripGlobal(string fullyQualified)
    {
        return fullyQualified.StartsWith("global::", StringComparison.Ordinal)
            ? fullyQualified.Substring("global::".Length)
            : fullyQualified;
    }

    private static string Identity(ITypeSymbol type)
    {
        return type.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString();
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
        IReadOnlyList<ServiceEdgesModel> serviceEdges)
    {
        return new List<ArtifactModel>
        {
            new ArtifactModel("requisite-manifest.json", RenderManifest(id, name, version, products, requisiteAssemblies, serviceEdges)),
        };
    }

    private static string RenderManifest(
        string id,
        string name,
        string version,
        IReadOnlyList<ProductModel> products,
        IReadOnlyList<string> requisiteAssemblies,
        IReadOnlyList<ServiceEdgesModel> serviceEdges)
    {
        var json = new StringBuilder();
        json.Append("{\n");
        json.Append("  \"boutique\": ").Append(JsonString(id)).Append(",\n");
        json.Append("  \"name\": ").Append(JsonString(name)).Append(",\n");
        json.Append("  \"version\": ").Append(JsonString(version)).Append(",\n");
        json.Append("  \"products\": ").Append(JsonArray(products.Select(p => p.DisplayName))).Append(",\n");
        json.Append("  \"requisiteAssemblies\": ").Append(JsonArray(requisiteAssemblies)).Append(",\n");
        json.Append("  \"services\": [");
        for (var i = 0; i < serviceEdges.Count; i++)
        {
            var service = serviceEdges[i];
            json.Append(i == 0 ? "\n" : ",\n");
            json.Append("    { \"identity\": ").Append(JsonString(service.Identity))
                .Append(", \"requisites\": ").Append(JsonArray(service.Requisites.Items)).Append(" }");
        }

        json.Append(serviceEdges.Count == 0 ? "]\n" : "\n  ]\n");
        json.Append("}\n");
        return json.ToString();
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

        if (registration.IsProduct)
        {
            yield return new BH.ProductForward
            {
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
    bool IsHostedService,
    bool IsProduct = false);

internal sealed record ApiEndpointModel(string EndpointsType, string MapMethod);

internal sealed record ArtifactModel(string FileName, string Content);

internal sealed record ServiceEdgesModel(string Identity, EquatableArray<string> Requisites);

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
