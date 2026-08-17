using Atelier.Build.Analysis;
using Atelier.Build.Discovery;
using Atelier.Build.Pipeline;
using Atelier.Build.Utils;
using Spectre.Console;
using Templar.Rendering;
using Templar.Presets;
using T = Atelier.Build.Templates.Program;

namespace Atelier.Build.Generation;

public class ProgramGenerator
{
    private static readonly Compositor EMPTY = new Verbatim { Text = string.Empty };

    private readonly BuildContext _context;

    private static bool IsValidTypeName(string? value)
    {
        return Atelier.Build.Utils.GeneratorText.IsValidTypeName(value);
    }

    private static string ToCSharpLiteralBody(string? value)
    {
        return Atelier.Build.Utils.GeneratorText.EscapeCSharpLiteral(value);
    }

    public ProgramGenerator(BuildContext context)
    {
        _context = context;
    }

    public async Task<string> GenerateAsync(
        BoutiqueYamlSchema schema,
        ProductDependencyGraph dependencyGraph,
        ResolvedBoutique resolved,
        string outputDirectory,
        string compiledAssembliesDirectory)
    {
        var boutiqueName = Naming.ToBoutiqueAssemblyIdentifier(schema.Name);

        if (!IsValidTypeName(boutiqueName))
        {
            AnsiConsole.MarkupLine($"[red]Error: refusing to generate Program.cs for boutique with invalid name (yields invalid namespace identifier): {Markup.Escape(schema.Name)}[/]");
            throw new InvalidOperationException($"Boutique name '{schema.Name}' yields an invalid namespace identifier '{boutiqueName}'.");
        }

        var code = new T.Program
        {
            Usings = RenderUsings(schema, boutiqueName),
            AssemblyLoaderCall = new T.AssemblyLoaderCall { BoutiqueName = boutiqueName },
            BuilderCreation = new T.BuilderCreation(),
            KestrelConfiguration = RenderKestrel(resolved),
            InstanceIdAndMetrics = new T.InstanceIdAndMetrics { ModeName = ToCSharpLiteralBody(schema.Name.Replace("atelier-", string.Empty)) },
            CoreServiceRegistration = new T.CoreServiceRegistration(),
            InfrastructureSetup = schema.Infrastructure?.SignalR?.Enabled == true
                ? (Compositor)new T.SignalRSetup()
                : EMPTY,
            AutoDiscovery = RenderAtelierRegistrations(dependencyGraph, compiledAssembliesDirectory),
            FallbackServiceRegistrations = RenderFallbackServices(schema),
            ExplicitServiceRegistrations = RenderExplicitServices(schema),
            Capabilities = RenderCapabilities(schema),
            HealthChecks = new T.HealthChecks { BoutiqueManifest = RenderBoutiqueManifest(schema) },
            CustomServiceConfiguration = RenderCustomServiceConfiguration(schema, boutiqueName),
            AppBuilding = new T.AppBuilding(),
            AttacheHostConfiguration = RenderAttacheHostConfig(schema),
            BoutiqueManifestCreation = EMPTY,
            EndpointMapping = RenderEndpointMapping(schema, resolved, boutiqueName),
            LifecycleHandlers = new T.LifecycleHandlers { SchemaName = ToCSharpLiteralBody(schema.Name) },
            RunCommand = new T.RunCommand(),
        }.Render();

        var outputPath = Path.Combine(outputDirectory, "Program.g.cs");
        await File.WriteAllTextAsync(outputPath, code).ConfigureAwait(false);

        if (_context.Verbose)
        {
            AnsiConsole.MarkupLine($"[dim]    → Generated Program.g.cs ({code.Split('\n').Length} lines)[/]");
        }

        return outputPath;
    }

    private static Compositor RenderUsings(BoutiqueYamlSchema schema, string boutiqueName)
    {
        var hasGravity = schema.Kestrel?.Endpoints?.Any(e =>
            e.Name == "gravity" || e.Name == "cluster") ?? false;

        var extra = new List<Compositor>();
        if (hasGravity)
        {
            extra.Add(new Using { Name = "EventHorizon.Cluster" });
            extra.Add(new Using { Name = "EventHorizon.Cluster.Hosting" });
        }
        if (schema.Infrastructure?.SignalR?.Enabled == true)
        {
            extra.Add(new Using { Name = "Microsoft.AspNetCore.SignalR" });
        }
        if (schema.Infrastructure?.Network?.Enabled == true)
        {
            extra.Add(new Using { Name = "Atelier.Framework.Network.Middleware" });
        }
        if (schema.Products is not null)
        {
            foreach (var asm in schema.Products
                .Where(p => !string.IsNullOrEmpty(p.Assembly))
                .Select(p => p.Assembly!)
                .Distinct()
                .OrderBy(a => a))
            {
                extra.Add(new Using { Name = $"{asm}.Products" });
            }
        }

        return new T.Usings
        {
            BoutiqueName = boutiqueName,
            ExtraUsings = Sequence.Lines(extra),
        };
    }

    private static Compositor RenderKestrel(ResolvedBoutique resolved)
    {
        if (resolved.Ports?.AllEndpoints is null || resolved.Ports.AllEndpoints.Count == 0)
        {
            return new T.KestrelDefault();
        }

        var portVars = Sequence.Lines(resolved.Ports.AllEndpoints.Select(e => (Compositor)new T.KestrelPortVar
            {
                VarName = $"{e.Name}Port",
                EnvVar = $"{e.Name.ToUpperInvariant()}_PORT",
                Name = e.Name,
                Port = e.Port,
            }));

        Compositor enableHttpsVar = resolved.Ports.AllEndpoints.Any(e => e.Tls != null)
            ? new T.EnableHttpsDeclaration()
            : EMPTY;

        var listenBlocks = Sequence.Lines(resolved.Ports.AllEndpoints
            .Select(RenderEndpointListen)
            .Where(c => c is not null)
            .Cast<Compositor>());

        return new T.Kestrel
        {
            PortVars = portVars,
            EnableHttpsVar = enableHttpsVar,
            ListenBlocks = listenBlocks,
        };
    }

    private static Compositor? RenderEndpointListen(ResolvedEndpoint endpoint)
    {
        var protocol = endpoint.Protocol.ToLowerInvariant() switch
        {
            "http1-only" => "Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1",
            "http2-only" => "Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2",
            _            => "Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2"
        };
        var varName = $"{endpoint.Name}Port";

        if (endpoint.Tls is null)
        {
            if (endpoint.Protocol.ToLowerInvariant() == "udp")
            {
                return null;
            }
            return new T.KestrelListenPlain { VarName = varName, Protocol = protocol };
        }

        Compositor fallbackBlock = new T.KestrelListenTlsFailClosed { EndpointName = ToCSharpLiteralBody(endpoint.Name) };

        if (!string.IsNullOrEmpty(endpoint.Tls.CertPath) && !string.IsNullOrEmpty(endpoint.Tls.KeyPath))
        {
            return new T.KestrelListenTlsFile
            {
                CertPath = ToCSharpLiteralBody(endpoint.Tls.CertPath),
                KeyPath = ToCSharpLiteralBody(endpoint.Tls.KeyPath),
                VarName = varName,
                Protocol = protocol,
                FallbackBlock = fallbackBlock,
            };
        }

        if (!string.IsNullOrEmpty(endpoint.Tls.CertPathEnv))
        {
            if (string.IsNullOrEmpty(endpoint.Tls.CertPasswordEnv))
            {
                throw new InvalidOperationException(
                    $"TLS endpoint '{endpoint.Name}' sets cert_path_env but no cert_password_env; both environment variable names are required for env-sourced certificates.");
            }

            return new T.KestrelListenTlsEnv
            {
                CertPathEnv = ToCSharpLiteralBody(endpoint.Tls.CertPathEnv),
                CertPasswordEnv = ToCSharpLiteralBody(endpoint.Tls.CertPasswordEnv),
                VarName = varName,
                Protocol = protocol,
                FallbackBlock = fallbackBlock,
            };
        }

        return null;
    }

    private static Compositor RenderFallbackServices(BoutiqueYamlSchema schema)
    {
        var production = schema.Services?.Any(s =>
            s.Interface?.Contains("IOfferingProvider") == true &&
            s.Implementation?.Contains("ServiceProviderOfferingProvider") == true) ?? false;

        Compositor offeringProvider = production
            ? new T.OfferingProviderProduction()
            : new T.OfferingProviderNull();

        return new T.FallbackServices { OfferingProvider = offeringProvider };
    }

    private static IComposable RenderAtelierRegistrations(
        ProductDependencyGraph dependencyGraph,
        string compiledAssembliesDirectory)
    {
        var registrations = InfrastructureRegistrationScanner.Scan(dependencyGraph, compiledAssembliesDirectory);
        var items = new List<Compositor>();
        foreach (var reg in registrations)
        {
            if (!IsValidTypeName(reg.Implementation))
            {
                continue;
            }

            if (reg.HasInterface
                && IsValidTypeName(reg.ServiceType))
            {
                items.Add(new T.ServiceWithInterface
                {
                    Lifetime = reg.Lifetime,
                    Interface = reg.ServiceType,
                    Implementation = reg.Implementation,
                });
            }
            else
            {
                items.Add(new T.ServiceImplOnly
                {
                    Lifetime = reg.Lifetime,
                    Implementation = reg.Implementation,
                });
            }

            if (reg.IsHostedService)
            {
                items.Add(new T.HostedService { Implementation = reg.ServiceType });
            }
        }

        return Sequence.Lines(items);
    }

    private static IComposable RenderExplicitServices(BoutiqueYamlSchema schema)
    {
        if (schema.Services is null || schema.Services.Count == 0)
        {
            return EMPTY;
        }

        var items = new List<Compositor>();
        foreach (var service in schema.Services)
        {
            foreach (var c in RenderServiceRegistration(service))
            {
                items.Add(c);
            }
        }

        return Sequence.Lines(items);
    }

    private static IEnumerable<Compositor> RenderServiceRegistration(ServiceRegistrationYaml service)
    {
        var lifetime = service.Lifetime switch
        {
            "Singleton" => "AddSingleton",
            "Scoped"    => "AddScoped",
            "Transient" => "AddTransient",
            _           => "AddScoped"
        };

        var hasInterface = !string.IsNullOrEmpty(service.Interface);

        if (!IsValidTypeName(service.Implementation))
        {
            AnsiConsole.MarkupLine($"[yellow]Warning: skipping service registration with invalid implementation type name: {Markup.Escape(service.Implementation ?? string.Empty)}[/]");
            yield break;
        }

        if (hasInterface
            && !IsValidTypeName(service.Interface))
        {
            AnsiConsole.MarkupLine($"[yellow]Warning: skipping service registration with invalid interface type name: {Markup.Escape(service.Interface ?? string.Empty)}[/]");
            yield break;
        }

        Compositor primary = hasInterface
            ? new T.ServiceWithInterface
            {
                Lifetime = lifetime,
                Interface = service.Interface,
                Implementation = service.Implementation,
            }
            : new T.ServiceImplOnly
            {
                Lifetime = lifetime,
                Implementation = service.Implementation,
            };

        yield return primary;

        if (service.HostedService)
        {
            yield return new T.HostedService { Implementation = service.Implementation };
        }
    }

    private static IComposable RenderCapabilities(BoutiqueYamlSchema schema)
    {
        var blocks = new List<Compositor>();

        if (schema.Capabilities?.Grpc?.Enabled == true)
        {
            var options = new List<Compositor>();
            if (schema.Capabilities.Grpc.MaxReceiveMessageSize.HasValue)
            {
                options.Add(new T.GrpcOption
                {
                    Name = "MaxReceiveMessageSize",
                    Value = schema.Capabilities.Grpc.MaxReceiveMessageSize.Value,
                });
            }
            if (schema.Capabilities.Grpc.MaxSendMessageSize.HasValue)
            {
                options.Add(new T.GrpcOption
                {
                    Name = "MaxSendMessageSize",
                    Value = schema.Capabilities.Grpc.MaxSendMessageSize.Value,
                });
            }

            blocks.Add(new T.Grpc { GrpcOptions = Sequence.Lines(options) });
        }

        if (schema.Capabilities?.Rest?.Enabled == true)
        {
            blocks.Add(new T.Rest());
        }

        if (schema.Capabilities?.WebSocket?.Enabled == true)
        {
            blocks.Add(new T.WebSocketCors());
        }

        return new Sequence(blocks, string.Empty);
    }

    private static IComposable RenderCustomServiceConfiguration(BoutiqueYamlSchema schema, string boutiqueName)
    {
        var gravity = schema.Kestrel?.Endpoints?.FirstOrDefault(e =>
            e.Name == "gravity" || e.Name == "cluster");

        Compositor gravityBlock = gravity is null ? EMPTY : RenderGravityCluster(schema.Name);

        return new Sequence(new[]
            {
                gravityBlock,
                new T.ExtensionsConfigureServices { BoutiqueName = boutiqueName },
            },
            string.Empty);
    }

    private static Compositor RenderGravityCluster(string schemaName)
    {
        var serviceName = schemaName.Replace("atelier-", string.Empty).ToLowerInvariant();
        var capabilities =
            (serviceName.Contains("pond") || serviceName.Contains("storage"))
                ? new[]
                  {
                      "EventHorizon.Cluster.NodeCapabilities.Storage",
                      "EventHorizon.Cluster.NodeCapabilities.Coordinator",
                  }
            : (serviceName.Contains("axiom") || serviceName.Contains("query"))
                ? new[] { "EventHorizon.Cluster.NodeCapabilities.Query" }
            : (serviceName.Contains("ws") || serviceName.Contains("ring") || serviceName.Contains("render"))
                ? new[] { "EventHorizon.Cluster.NodeCapabilities.Render" }
            : Array.Empty<string>();

        var assignments = Sequence.Lines(capabilities.Select(c => (Compositor)new T.CapabilityAssignment { Capability = c }));

        return new T.GravityCluster { CapabilityAssignments = assignments };
    }

    private static Compositor RenderAttacheHostConfig(BoutiqueYamlSchema schema)
    {
        Compositor maxMemory = schema.Resources?.MaxMemoryBytes.HasValue == true
            ? new T.LongLiteral { Value = schema.Resources.MaxMemoryBytes.Value }
            : new T.DefaultMaxMemory();

        Compositor maxCpu = schema.Resources?.MaxCpuPercent.HasValue == true
            ? new Verbatim { Text = schema.Resources.MaxCpuPercent.Value.ToString() }
            : new Verbatim { Text = "80" };

        return new T.AttacheHostConfig { MaxMemory = maxMemory, MaxCpu = maxCpu };
    }

    private static Compositor RenderBoutiqueManifest(BoutiqueYamlSchema schema)
    {
        Compositor productsBlock;
        if (schema.Products is null || schema.Products.Count == 0)
        {
            productsBlock = new T.EmptyProductsList();
        }
        else
        {
            var entryItems = new List<Compositor>();
            foreach (var p in schema.Products)
            {
                var entry = RenderProductManifest(p);
                if (entry is not null)
                {
                    entryItems.Add(entry);
                }
            }
            productsBlock = entryItems.Count == 0
                ? new T.EmptyProductsList()
                : new T.ProductsList { Entries = Sequence.Lines(entryItems) };
        }

        return new T.BoutiqueManifest
        {
            BoutiqueId = ToCSharpLiteralBody(schema.Name),
            Name = ToCSharpLiteralBody(schema.Description ?? schema.Name),
            Description = ToCSharpLiteralBody(schema.Description ?? string.Empty),
            Version = ToCSharpLiteralBody(schema.Version),
            SupportsRest = (schema.Capabilities?.Rest?.Enabled == true) ? "true" : "false",
            SupportsGrpc = (schema.Capabilities?.Grpc?.Enabled == true) ? "true" : "false",
            SupportsWebSocket = (schema.Capabilities?.WebSocket?.Enabled == true) ? "true" : "false",
            SupportsGraphQL = (schema.Capabilities?.GraphQL?.Enabled == true) ? "true" : "false",
            ProductsBlock = productsBlock,
        };
    }

    private static T.ProductManifest? RenderProductManifest(ProductYaml product)
    {
        if (!IsValidTypeName(product.Type))
        {
            AnsiConsole.MarkupLine($"[yellow]Warning: skipping product manifest with invalid product type name: {Markup.Escape(product.Type)}[/]");
            return null;
        }

        Compositor configBlock;
        if (product.Config is null || product.Config.Count == 0)
        {
            configBlock = new T.EmptyProductConfig();
        }
        else
        {
            var entries = Sequence.Lines(product.Config.OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
                .Select(kvp => (Compositor)new T.ProductConfigEntry
                {
                    Key = ToCSharpLiteralBody(kvp.Key),
                    Value = FormatConfigValue(kvp.Value),
                }));
            configBlock = new T.ProductConfig { Entries = entries };
        }

        return new T.ProductManifest
        {
            ProductType = product.Type,
            AutoStart = product.AutoStart ? "true" : "false",
            ConfigBlock = configBlock,
        };
    }

    private static Compositor RenderEndpointMapping(BoutiqueYamlSchema schema, ResolvedBoutique resolved, string boutiqueName)
    {
        Compositor websocketSetup = schema.Capabilities?.WebSocket?.Enabled == true
            ? new T.EndpointWebsocket()
            : EMPTY;

        Compositor staticFiles = schema.StaticFiles?.Enabled == true
            ? new T.UseStaticFiles { RootPath = ToCSharpLiteralBody(schema.StaticFiles.RootPath) }
            : EMPTY;

        Compositor rest = schema.Capabilities?.Rest?.Enabled == true
            ? new T.EndpointRest { BasePath = ToCSharpLiteralBody(schema.Capabilities.Rest.BasePath) }
            : EMPTY;

        var grpcItems = new List<Compositor>();
        foreach (var g in schema.GrpcServices ?? new())
        {
            if (!IsValidTypeName(g.Implementation))
            {
                AnsiConsole.MarkupLine($"[yellow]Warning: skipping grpc service mapping with invalid implementation type name: {Markup.Escape(g.Implementation)}[/]");
                continue;
            }
            grpcItems.Add(new T.EndpointGrpcService { Implementation = g.Implementation });
        }
        var grpcServices = Sequence.Lines(grpcItems);

        var infoEndpoint = new T.EndpointInfo
        {
            BoutiqueId = ToCSharpLiteralBody(schema.Name),
            Name = ToCSharpLiteralBody(schema.Description ?? schema.Name),
            Version = ToCSharpLiteralBody(schema.Version),
        };

        Compositor contextExtraction = schema.Infrastructure?.Network?.Enabled == true
            ? new T.ContextExtraction()
            : EMPTY;

        Compositor scopeEnforcement = schema.Infrastructure?.Network?.Enabled == true
            ? new T.ScopeEnforcement()
            : EMPTY;

        return new T.EndpointMapping
        {
            ContextExtraction = contextExtraction,
            ScopeEnforcement = scopeEnforcement,
            WebsocketSetup = websocketSetup,
            StaticFiles = staticFiles,
            Rest = rest,
            GrpcServices = grpcServices,
            HealthPath = ToCSharpLiteralBody(resolved.Health.LivenessPath),
            ReadinessPath = ToCSharpLiteralBody(resolved.Health.ReadinessPath),
            DefaultRedirect = ToCSharpLiteralBody(schema.StaticFiles?.DefaultFile ?? resolved.Health.LivenessPath),
            InfoEndpoint = infoEndpoint,
            MetricsEndpoint = new T.EndpointMetrics(),
            ExtensionsMap = new T.EndpointExtensions { BoutiqueName = boutiqueName },
        };
    }

    private static Compositor FormatConfigValue(object value) => value switch
    {
        string s => new T.StringLiteral { Value = ToCSharpLiteralBody(s) },
        bool b   => new Verbatim { Text = b ? "true" : "false" },
        int i    => new Verbatim { Text = i.ToString() },
        long l   => new T.LongLiteral { Value = l },
        double d => new T.DoubleLiteral { Value = d },
        float f  => new T.FloatLiteral { Value = f },
        _        => new T.StringLiteral { Value = ToCSharpLiteralBody(value.ToString() ?? string.Empty) },
    };
}
