using Atelier.Framework.Attache.Lifecycle;
using Atelier.Framework.Context;
using Atelier.Framework.Network.Middleware;
using Atelier.Framework.Observability;
using Atelier.Framework.Observability.Strategy;
using Atelier.Framework.Offering;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Atelier.Framework.Attache.Hosting;

public static class BoutiqueHost
{
    private const string CORS_POLICY = "AllowAll";
    private const string LIVENESS_TAG = "liveness";
    private const string READINESS_TAG = "readiness";
    private const string ARTIFACTS_SWITCH = "--atelier-artifacts";

    public static async Task RunAsync(string[] args, BoutiqueHostDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (args.Length == 2 && args[0] == ARTIFACTS_SWITCH)
        {
            await WriteArtifactsAsync(args[1], definition.Artifacts).ConfigureAwait(false);
            return;
        }

        var builder = WebApplication.CreateBuilder(args);

        var options = new BoutiqueHostOptions();
        builder.Configuration.GetSection(BoutiqueHostOptions.SECTION).Bind(options);

        ConfigureKestrel(builder);

        var instanceId = Environment.GetEnvironmentVariable("INSTANCE_ID") ?? Guid.NewGuid().ToString("N")[..8];
        ApplicationMetrics.Initialize(instanceId, definition.Id);
        WriteBanner(definition.Id, instanceId);

        var manifest = BuildManifest(definition, options);

        RegisterCoreServices(builder.Services);
        definition.RegisterServices(builder.Services);
        RegisterCapabilities(builder.Services, options.Capabilities);
        builder.Services.AddAtelierBoutiqueStartup(manifest);
        builder.Services.AddHostedService<BoutiqueDrainService>();
        definition.ConfigureServices(builder.Services);

        var app = builder.Build();

        app.Services.GetRequiredService<AttacheHost>().Configure(new AttacheConfiguration
        {
            AutoStartBoutiques = true,
            ResourceLimits = new AttacheResourceLimits
            {
                MaxBoutiques = 1,
                MaxMemoryBytes = options.MaxMemoryBytes,
                MaxCpuPercent = options.MaxCpuPercent,
            },
        });

        MapEndpoints(app, definition, options, manifest, instanceId);
        definition.ConfigureApplication(app);
        RegisterLifecycle(app, definition.Id, instanceId);

        await app.RunAsync().ConfigureAwait(false);
    }

    private static async Task WriteArtifactsAsync(string directory, IReadOnlyList<BoutiqueArtifact> artifacts)
    {
        Directory.CreateDirectory(directory);
        foreach (var artifact in artifacts)
        {
            await File.WriteAllTextAsync(Path.Combine(directory, artifact.FileName), artifact.Content).ConfigureAwait(false);
        }
    }

    private static void ConfigureKestrel(WebApplicationBuilder builder)
    {
        if (builder.Configuration.GetSection("Kestrel:Endpoints").GetChildren().Any())
        {
            return;
        }

        var httpPort = ReadPort("HTTP_PORT", 8080);
        var grpcPort = ReadPort("GRPC_PORT", 8081);
        var metricsPort = ReadPort("METRICS_PORT", 9090);

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.ListenAnyIP(httpPort, listen => listen.Protocols = HttpProtocols.Http1AndHttp2);
            kestrel.ListenAnyIP(grpcPort, listen => listen.Protocols = HttpProtocols.Http2);
            kestrel.ListenAnyIP(metricsPort, listen => listen.Protocols = HttpProtocols.Http1);
        });
    }

    private static int ReadPort(string environmentVariable, int fallback)
    {
        return int.TryParse(Environment.GetEnvironmentVariable(environmentVariable), out var port) ? port : fallback;
    }

    private static void WriteBanner(string boutiqueId, string instanceId)
    {
        Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine($"║   ATELIER HOST - {boutiqueId.ToUpperInvariant(),-36} BOUTIQUE ║");
        Console.WriteLine($"║   Instance: {instanceId,-49} ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
    }

    private static BoutiqueManifest BuildManifest(BoutiqueHostDefinition definition, BoutiqueHostOptions options)
    {
        return new BoutiqueManifest
        {
            BoutiqueId = definition.Id,
            Name = definition.Name,
            Description = definition.Description,
            Version = definition.Version,
            Capabilities = new BoutiqueCapabilities
            {
                SupportsRest = options.Capabilities.Rest,
                SupportsGrpc = options.Capabilities.Grpc,
                SupportsWebSocket = options.Capabilities.WebSocket,
                SupportsGraphQL = options.Capabilities.GraphQL,
            },
            Products = definition.Products.ToList(),
        };
    }

    private static void RegisterCoreServices(IServiceCollection services)
    {
        services.AddSingleton<IContext>(_ => Context.Context.Empty);
        services.AddSingleton<ILoggingStrategy, ConsoleLoggingStrategy>();
        services.AddSingleton<ILogger>(sp => new Logger(sp.GetRequiredService<ILoggingStrategy>()));
    }

    private static void RegisterCapabilities(IServiceCollection services, BoutiqueHostCapabilityOptions capabilities)
    {
        if (capabilities.Grpc)
        {
            services.AddGrpc();
        }

        if (capabilities.Rest)
        {
            services.AddControllers();
        }

        if (capabilities.WebSocket)
        {
            services.AddCors(cors => cors.AddPolicy(CORS_POLICY, policy =>
                policy.SetIsOriginAllowed(origin =>
                        origin == "null"
                        || origin.StartsWith("http:", StringComparison.Ordinal)
                        || origin.StartsWith("https:", StringComparison.Ordinal))
                      .AllowAnyMethod()
                      .AllowAnyHeader()
                      .AllowCredentials()));
        }
    }

    private static void MapEndpoints(
        WebApplication app,
        BoutiqueHostDefinition definition,
        BoutiqueHostOptions options,
        BoutiqueManifest manifest,
        string instanceId)
    {
        if (options.NetworkEnabled)
        {
            app.UseContextExtraction();
            app.UseScopeEnforcement();
        }

        if (options.Capabilities.WebSocket)
        {
            app.UseCors(CORS_POLICY);
            app.UseWebSockets();
        }

        if (options.Capabilities.Rest)
        {
            app.MapControllers();
            definition.MapApiEndpoints(app.MapGroup(options.RestBasePath));
        }

        MapHealthChecks(app, options);
        app.MapGet("/", () => Results.Redirect(options.LivenessPath));

        app.MapGet("/info", () => Results.Ok(new
        {
            boutique = new
            {
                id = definition.Id,
                name = definition.Name,
                version = definition.Version,
                mode = definition.Id,
                instance = instanceId,
            },
            capabilities = manifest.Capabilities,
            products = manifest.Products.Select(p => new { type = p.ProductType?.Name ?? p.ProductTypeName, autoStart = p.AutoStart }),
            timestamp = DateTime.UtcNow,
        }));

        app.MapGet("/metrics", WriteMetricsAsync);
    }

    private static void MapHealthChecks(WebApplication app, BoutiqueHostOptions options)
    {
        if (string.Equals(options.LivenessPath, options.ReadinessPath, StringComparison.Ordinal))
        {
            app.MapHealthChecks(options.LivenessPath, new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains(LIVENESS_TAG) || check.Tags.Contains(READINESS_TAG),
            });
            return;
        }

        app.MapHealthChecks(options.LivenessPath, new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(LIVENESS_TAG),
        });
        app.MapHealthChecks(options.ReadinessPath, new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(READINESS_TAG),
        });
    }

    private static async Task WriteMetricsAsync(HttpContext httpContext, IServiceProvider services)
    {
        var accept = httpContext.Request.Headers.Accept.ToString();

        if (accept.Contains("text/plain", StringComparison.Ordinal)
            || accept.Contains("application/openmetrics-text", StringComparison.Ordinal)
            || httpContext.Request.Query.ContainsKey("prometheus"))
        {
            httpContext.Response.ContentType = "text/plain; version=0.0.4; charset=utf-8";
            await Prometheus.Metrics.DefaultRegistry
                .CollectAndExportAsTextAsync(httpContext.Response.Body, httpContext.RequestAborted)
                .ConfigureAwait(false);
            return;
        }

        var healthReport = await services.GetRequiredService<AttacheHost>()
            .GetHealthReportAsync()
            .ConfigureAwait(false);
        if (!healthReport.IsSuccess)
        {
            httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return;
        }

        await httpContext.Response.WriteAsJsonAsync(healthReport.Data).ConfigureAwait(false);
    }

    private static void RegisterLifecycle(WebApplication app, string boutiqueId, string instanceId)
    {
        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();

        lifetime.ApplicationStarted.Register(() =>
        {
            Console.WriteLine();
            Console.WriteLine($"{boutiqueId} started");
            Console.WriteLine($"  Instance: {instanceId}");
        });

        lifetime.ApplicationStopping.Register(() => Console.WriteLine($"Shutting down {boutiqueId}..."));
    }
}
