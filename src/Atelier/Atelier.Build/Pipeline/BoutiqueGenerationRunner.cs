using System.Diagnostics;
using Atelier.Build.Analysis;
using Atelier.Build.Discovery;
using Atelier.Build.Generation;
using Atelier.Build.Utils;

namespace Atelier.Build.Pipeline;

public sealed record GeneratedBoutiques(
    List<BoutiqueYamlSchema> Schemas,
    List<ResolvedBoutique> Resolved);

public sealed class BoutiqueGenerationRunner
{
    private readonly BuildContext _context;
    private readonly BuildPresenter _presenter;
    private readonly OutputPathResolver _outputPathResolver;

    public BoutiqueGenerationRunner(BuildContext context, BuildPresenter presenter)
    {
        _context = context;
        _presenter = presenter;
        _outputPathResolver = new OutputPathResolver();
    }

    public async Task<BuildResult> ExecuteAsync(List<string> artifacts)
    {
        var stopwatch = Stopwatch.StartNew();
        _presenter.GenerationHeader();

        var boutiquesDir = _context.BoutiquesDirectory;

        if (!Directory.Exists(boutiquesDir))
        {
            Directory.CreateDirectory(boutiquesDir);
            _presenter.CreatedBoutiquesDirectory(boutiquesDir);
        }

        var boutiqueDefinitions = await DiscoverGenerationBoutiquesAsync().ConfigureAwait(false);

        if (boutiqueDefinitions.Count == 0)
        {
            _presenter.NoBoutiquesToGenerate();
            return BuildResult.Success(artifacts, []);
        }

        var compiledAssembliesDir = Path.Combine(_context.BuildOutputDirectory, "assemblies");

        if (!Directory.Exists(compiledAssembliesDir))
        {
            _presenter.AssembliesNotCompiledWarning();
        }

        var generated = await GenerateBoutiqueProjectsAsync(
            boutiqueDefinitions,
            boutiquesDir,
            compiledAssembliesDir,
            artifacts).ConfigureAwait(false);

        await GenerateOrchestrationComposeAsync(generated.Schemas, generated.Resolved, artifacts).ConfigureAwait(false);

        var networkPolicyPath = await new NetworkPolicyGenerator(_context)
            .GenerateAsync(generated.Resolved)
            .ConfigureAwait(false);
        if (networkPolicyPath != null)
        {
            artifacts.Add(networkPolicyPath);
        }

        var composePath = Path.Combine(_context.SolutionRoot, "docker-compose.yml");
        var topologyViolations = NetworkTopologyValidator.ValidateComposeFile(composePath);
        if (topologyViolations.Count > 0)
        {
            _presenter.NetworkTopologyViolations(topologyViolations);
            return BuildResult.Failure($"Network topology validation failed: {topologyViolations.Count} violation(s)");
        }

        await GenerateBenchmarkContainersAsync(artifacts).ConfigureAwait(false);

        await GenerateTestContainersAsync(artifacts).ConfigureAwait(false);

        _presenter.GenerationSummary(boutiqueDefinitions.Count, artifacts.Count, stopwatch.Elapsed.TotalSeconds);

        return BuildResult.Success(artifacts, []);
    }

    private async Task<IReadOnlyList<BoutiqueDefinition>> DiscoverGenerationBoutiquesAsync()
    {
        _presenter.DiscoveringBoutiques();

        var discoverer = new BoutiqueDiscoverer(_context);
        var boutiqueDefinitions = await discoverer.DiscoverAsync().ConfigureAwait(false);

        if (boutiqueDefinitions.Count > 0)
        {
            _presenter.FoundBoutiquesWithPorts(boutiqueDefinitions);
        }

        return boutiqueDefinitions;
    }

    private async Task<GeneratedBoutiques> GenerateBoutiqueProjectsAsync(
        IReadOnlyList<BoutiqueDefinition> boutiqueDefinitions,
        string boutiquesDir,
        string compiledAssembliesDir,
        List<string> artifacts)
    {
        _presenter.GeneratingBoutiqueProjects();

        var allSchemas = new List<BoutiqueYamlSchema>();
        var allResolved = new List<ResolvedBoutique>();

        foreach (var definition in boutiqueDefinitions)
        {
            var outputDir = _outputPathResolver.ResolveBoutiqueOutputDirectory(definition, boutiquesDir);
            Directory.CreateDirectory(outputDir);

            var boutiqueName = Path.GetFileName(outputDir);
            _presenter.ProcessingBoutique(boutiqueName);

            try
            {
                var schema = ConvertToSchema(definition);
                allSchemas.Add(schema);

                var dependencyGraph = AnalyzeBoutiqueDependencies(schema, compiledAssembliesDir);
                var resolved = BoutiqueResolver.Resolve(schema, dependencyGraph, compiledAssembliesDir, _context.Verbose);
                allResolved.Add(resolved);

                if (_context.DryRun)
                {
                    _presenter.BoutiqueDryRun(outputDir, Naming.ToPascalCase(boutiqueName));
                    continue;
                }

                await GenerateSingleBoutiqueAsync(schema, dependencyGraph, resolved, outputDir, compiledAssembliesDir, artifacts).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _presenter.BoutiqueGenerationError(ex.Message, ex, _context.Verbose);
            }
        }

        return new GeneratedBoutiques(allSchemas, allResolved);
    }

    private async Task GenerateSingleBoutiqueAsync(
        BoutiqueYamlSchema schema,
        ProductDependencyGraph dependencyGraph,
        ResolvedBoutique resolved,
        string outputDir,
        string compiledAssembliesDir,
        List<string> artifacts)
    {
        var contextAccessorPath = await GenerateDefaultContextAccessorAsync(schema, outputDir).ConfigureAwait(false);
        artifacts.Add(contextAccessorPath);
        _presenter.GeneratedArtifact(Path.GetFileName(contextAccessorPath));

        var stubPaths = await GenerateStubServicesAsync(schema, outputDir).ConfigureAwait(false);
        artifacts.AddRange(stubPaths);
        foreach (var stubPath in stubPaths)
        {
            _presenter.GeneratedArtifact(Path.GetFileName(stubPath));
        }

        var programPath = await GenerateProgramAsync(schema, dependencyGraph, resolved, outputDir).ConfigureAwait(false);
        artifacts.Add(programPath);
        _presenter.GeneratedProgram();

        var projectPath = await GenerateProjectFileAsync(schema, dependencyGraph, outputDir).ConfigureAwait(false);
        artifacts.Add(projectPath);
        _presenter.GeneratedArtifact(Path.GetFileName(projectPath));

        var loaderPath = await GenerateAssemblyLoaderForBoutiqueAsync(schema, dependencyGraph, outputDir, compiledAssembliesDir).ConfigureAwait(false);
        artifacts.Add(loaderPath);
        _presenter.GeneratedArtifact(Path.GetFileName(loaderPath));

        var dockerfilePath = await GenerateDockerfileAsync(schema, dependencyGraph, resolved, outputDir).ConfigureAwait(false);
        artifacts.Add(dockerfilePath);
        _presenter.GeneratedArtifact(Path.GetFileName(dockerfilePath));

        var boutiqueComposePath = Path.Combine(outputDir, "docker-compose.yml");
        var boutiqueComposeGenerator = new DockerComposeGenerator(_context);
        await boutiqueComposeGenerator.GenerateAsync([schema], [resolved], boutiqueComposePath, relativePathToRoot: "../..").ConfigureAwait(false);
        artifacts.Add(boutiqueComposePath);
        _presenter.GeneratedStandaloneCompose(Path.GetFileName(boutiqueComposePath));

        _presenter.BoutiqueDependencyStats(dependencyGraph.TotalAssemblyCount, dependencyGraph.TypeCount);
    }

    private async Task GenerateOrchestrationComposeAsync(
        List<BoutiqueYamlSchema> allSchemas,
        List<ResolvedBoutique> allResolved,
        List<string> artifacts)
    {
        _presenter.GeneratingCompose();

        try
        {
            var dockerComposePath = Path.Combine(_context.SolutionRoot, "docker-compose.yml");
            var composeGenerator = new DockerComposeGenerator(_context);
            var composePath = await composeGenerator.GenerateAsync(allSchemas, allResolved, dockerComposePath).ConfigureAwait(false);
            artifacts.Add(composePath);
            _presenter.GeneratedCompose(allSchemas.Count);
        }
        catch (Exception ex)
        {
            _presenter.ComposeWarning(ex.Message);
        }
    }

    private async Task GenerateBenchmarkContainersAsync(List<string> artifacts)
    {
        _presenter.GeneratingBenchmarkContainers();

        try
        {
            var benchmarkDiscoverer = new BenchmarkDiscoverer(_context);
            var benchmarkDefinitions = await benchmarkDiscoverer.DiscoverAsync().ConfigureAwait(false);

            if (benchmarkDefinitions.Count > 0)
            {
                _presenter.FoundBenchmarkProjects(benchmarkDefinitions.Count);

                var benchmarkGenerator = new BenchmarkDockerfileGenerator(_context);

                foreach (var benchDef in benchmarkDefinitions)
                {
                    var dockerfilePath = await benchmarkGenerator.GenerateAsync(benchDef).ConfigureAwait(false);
                    artifacts.Add(dockerfilePath);
                    _presenter.GeneratedBenchmarkDockerfile(Path.GetFileName(dockerfilePath));
                }

                var benchmarkComposePath = await benchmarkGenerator.GenerateDockerComposeAsync(benchmarkDefinitions).ConfigureAwait(false);
                artifacts.Add(benchmarkComposePath);
                _presenter.GeneratedBenchmarkCompose(benchmarkDefinitions.Count);
            }
            else
            {
                _presenter.NoBenchmarkProjects();
            }
        }
        catch (Exception ex)
        {
            _presenter.BenchmarkContainersWarning(ex.Message);
        }
    }

    private async Task GenerateTestContainersAsync(List<string> artifacts)
    {
        _presenter.GeneratingTestContainers();

        try
        {
            var testDiscoverer = new TestDiscoverer(_context);
            var testDefinitions = await testDiscoverer.DiscoverAsync().ConfigureAwait(false);

            if (testDefinitions.Count > 0)
            {
                var totalTestProjects = testDefinitions.Sum(d => d.TestProjectCount);
                _presenter.FoundTestSuites(testDefinitions.Count, totalTestProjects);

                var testGenerator = new TestDockerfileGenerator(_context);

                foreach (var testDef in testDefinitions)
                {
                    var dockerfilePath = await testGenerator.GenerateAsync(testDef).ConfigureAwait(false);
                    artifacts.Add(dockerfilePath);
                    _presenter.GeneratedTestDockerfile(Path.GetFileName(dockerfilePath), testDef.TestProjectCount);
                }

                var testComposePath = await testGenerator.GenerateDockerComposeAsync(testDefinitions).ConfigureAwait(false);
                artifacts.Add(testComposePath);
                _presenter.GeneratedTestCompose(testDefinitions.Count);
            }
            else
            {
                _presenter.NoTestProjects();
            }
        }
        catch (Exception ex)
        {
            _presenter.TestContainersWarning(ex.Message);
        }
    }

    private ProductDependencyGraph AnalyzeBoutiqueDependencies(BoutiqueYamlSchema schema, string compiledAssembliesDir)
    {
        var graph = new ProductDependencyGraph();

        if (!Directory.Exists(compiledAssembliesDir))
        {
            return graph;
        }

        if (schema.Products == null || schema.Products.Count == 0)
        {
            return graph;
        }

        using var analyzer = new ProductDependencyAnalyzer(_context);

        var products = schema.Products
            .Where(p => !string.IsNullOrEmpty(p.Assembly))
            .Select(p => (p.Type, p.Assembly!));

        return analyzer.AnalyzeProducts(products, compiledAssembliesDir);
    }

    private async Task<string> GenerateProgramAsync(
        BoutiqueYamlSchema schema,
        ProductDependencyGraph dependencyGraph,
        ResolvedBoutique resolved,
        string outputDirectory)
    {
        var generator = new ProgramGenerator(_context);
        return await generator.GenerateAsync(schema, dependencyGraph, resolved, outputDirectory).ConfigureAwait(false);
    }

    private async Task<string> GenerateProjectFileAsync(
        BoutiqueYamlSchema schema,
        ProductDependencyGraph dependencyGraph,
        string outputDirectory)
    {
        var generator = new ProjectFileGenerator(_context);
        return await generator.GenerateAsync(schema, dependencyGraph, outputDirectory, _context.SolutionRoot).ConfigureAwait(false);
    }

    private async Task<string> GenerateAssemblyLoaderForBoutiqueAsync(
        BoutiqueYamlSchema schema,
        ProductDependencyGraph dependencyGraph,
        string outputDirectory,
        string compiledAssembliesDir)
    {
        var generator = new PerBoutiqueAssemblyLoaderGenerator(_context);
        return await generator.GenerateAsync(schema, dependencyGraph, outputDirectory, compiledAssembliesDir).ConfigureAwait(false);
    }

    private async Task<string> GenerateDefaultContextAccessorAsync(
        BoutiqueYamlSchema schema,
        string outputDirectory)
    {
        var generator = new DefaultContextAccessorGenerator(_context);
        return await generator.GenerateAsync(schema, outputDirectory).ConfigureAwait(false);
    }

    private async Task<List<string>> GenerateStubServicesAsync(
        BoutiqueYamlSchema schema,
        string outputDirectory)
    {
        var generator = new StubServicesGenerator(_context);
        return await generator.GenerateAsync(schema, outputDirectory).ConfigureAwait(false);
    }

    private async Task<string> GenerateDockerfileAsync(
        BoutiqueYamlSchema schema,
        ProductDependencyGraph dependencyGraph,
        ResolvedBoutique resolved,
        string outputDirectory)
    {
        var generator = new DockerfileGenerator(_context);
        return await generator.GenerateAsync(schema, dependencyGraph, resolved, outputDirectory).ConfigureAwait(false);
    }

    private static BoutiqueYamlSchema ConvertToSchema(BoutiqueDefinition definition)
    {
        return new BoutiqueYamlSchema
        {
            Name = definition.Name,
            Version = definition.Version,
            Description = definition.Description,
            SubsystemName = definition.SubsystemName,
            Dependencies = definition.Dependencies.ToList(),
            Products = definition.Products?.ToList(),
            ProjectReferences = definition.ProjectReferences.ToList(),
            GrpcServices = definition.GrpcServices.Select(g => new GrpcServiceYaml
            {
                Implementation = g.Implementation,
                Assembly = g.Assembly
            }).ToList(),
            Kestrel = BuildKestrelEndpoints(definition.Ports),
            Infrastructure = new InfrastructureYaml
            {
                Postgres = definition.Infrastructure.PostgresEnabled ? new PostgresYaml { Enabled = true } : null,
                Redis = definition.Infrastructure.RedisEnabled ? new RedisYaml { Enabled = true } : null,
                Hangfire = definition.Infrastructure.HangfireEnabled ? new HangfireYaml { Enabled = true } : null,
                SignalR = definition.Infrastructure.SignalREnabled ? new SignalRYaml { Enabled = true } : null
            },
            Capabilities = new CapabilitiesYaml
            {
                Rest = new RestCapabilityYaml { Enabled = definition.Capabilities?.RestEnabled ?? true },
                Grpc = new GrpcCapabilityYaml { Enabled = definition.Capabilities?.GrpcEnabled ?? true },
                WebSocket = new WebSocketCapabilityYaml { Enabled = definition.Capabilities?.WebSocketEnabled ?? true }
            },
            Build = new BuildYaml
            {
                Configuration = definition.Build.Configuration,
                TreatWarningsAsErrors = definition.Build.TreatWarningsAsErrors,
                TargetFramework = "net10.0",
                AllowUnsafeBlocks = true,
                Protos = definition.Build.Protos?.ToList() ?? []
            },
            Docker = new DockerYaml
            {
                Ports = BuildPortsList(definition.Ports),
                PortMappings = BuildPortMappings(definition.Ports),
                Env = definition.Docker?.EnvironmentVariables?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                    ?? new Dictionary<string, string> { ["ASPNETCORE_ENVIRONMENT"] = "Production" },
                Volumes = definition.Docker?.Volumes?.ToList() ?? [],
                Command = definition.Docker?.Command?.ToList(),
                HealthCheck = definition.Docker?.HealthCheck != null
                    ? new DockerHealthCheckYaml
                    {
                        Path = definition.Docker.HealthCheck.Path,
                        Port = definition.Docker.HealthCheck.Port
                    }
                    : null
            },
            Health = new HealthYaml
            {
                Liveness = new HealthEndpointYaml
                {
                    Path = definition.Docker?.HealthCheck?.Path ?? "/health",
                    IntervalSeconds = 10
                },
                Readiness = new HealthEndpointYaml
                {
                    Path = definition.Docker?.HealthCheck?.Path ?? "/health/ready",
                    IntervalSeconds = 10,
                    StartupDelaySeconds = 5
                },
                Checks = [new HealthCheckYaml { Name = "self", Type = "builtin" }]
            },
            Resources = new ResourcesYaml
            {
                MaxMemoryBytes = definition.Resources?.MaxMemoryBytes ?? 4294967296,
                MaxCpuPercent = definition.Resources?.MaxCpuPercent ?? 80
            }
        };
    }

    private static List<int> BuildPortsList(PortConfiguration ports)
    {
        var portList = new List<int> { ports.Http, ports.Grpc, ports.Metrics };

        if (ports.Gravity.HasValue)
        {
            portList.Add(ports.Gravity.Value);
        }

        return portList;
    }

    private static Dictionary<int, int> BuildPortMappings(PortConfiguration ports)
    {
        var mappings = new Dictionary<int, int>
        {
            [ports.Http] = ports.Http,
            [ports.Grpc] = ports.Grpc,
            [ports.Metrics] = ports.Metrics
        };

        if (ports.Gravity.HasValue)
        {
            mappings[ports.Gravity.Value] = ports.Gravity.Value;
        }

        return mappings;
    }

    private static KestrelYaml BuildKestrelEndpoints(PortConfiguration ports)
    {
        var endpoints = new List<KestrelEndpointYaml>
        {
            new() { Name = "http", Port = ports.Http, Protocol = "http1-and-http2" },
            new() { Name = "grpc", Port = ports.Grpc, Protocol = "http2-only" },
            new() { Name = "metrics", Port = ports.Metrics, Protocol = "http1-only" }
        };

        if (ports.Gravity.HasValue)
        {
            endpoints.Add(new KestrelEndpointYaml
            {
                Name = "gravity",
                Port = ports.Gravity.Value,
                Protocol = "udp"
            });
        }

        return new KestrelYaml { Endpoints = endpoints };
    }
}
