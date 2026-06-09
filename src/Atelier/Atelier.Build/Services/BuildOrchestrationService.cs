using System.Diagnostics;
using Atelier.Build.Discovery;
using Atelier.Build.Pipeline;
using Atelier.Build.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atelier.Build.Services;

public class BuildOrchestrationService : IBuildOrchestrationService
{
    private readonly IBoutiqueDiscoveryService _discoveryService;
    private readonly IBoutiqueCompilationService _compilationService;
    private readonly IDependencyAnalysisService _dependencyAnalysisService;
    private readonly IBoutiqueGenerationService _generationService;
    private readonly IArtifactManagementService _artifactManagementService;
    private readonly ILogger<BuildOrchestrationService> _logger;

    public BuildOrchestrationService(
        IBoutiqueDiscoveryService discoveryService,
        IBoutiqueCompilationService compilationService,
        IDependencyAnalysisService dependencyAnalysisService,
        IBoutiqueGenerationService generationService,
        IArtifactManagementService artifactManagementService,
        ILogger<BuildOrchestrationService>? logger = null)
    {
        _discoveryService = discoveryService;
        _compilationService = compilationService;
        _dependencyAnalysisService = dependencyAnalysisService;
        _generationService = generationService;
        _artifactManagementService = artifactManagementService;
        _logger = logger ?? NullLogger<BuildOrchestrationService>.Instance;
    }

    public async Task<BuildResult> ExecuteBuildAsync(
        BuildOptions options,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await ExecuteBuildCoreAsync(options, cancellationToken).ConfigureAwait(false);
        result = result with { Duration = stopwatch.Elapsed };

        if (result.IsSuccess)
        {
            _logger.LogInformation(
                "Build complete: {Boutiques} boutiques, {Assemblies} total assemblies, {Artifacts} artifacts in {Duration:F2}s",
                result.BuiltBoutiques.Count,
                result.BuiltBoutiques.Sum(b => b.RequisiteAssemblies.Count),
                result.GeneratedArtifacts.Count,
                result.Duration.TotalSeconds);
        }

        return result;
    }

    private async Task<BuildResult> ExecuteBuildCoreAsync(
        BuildOptions options,
        CancellationToken cancellationToken)
    {
        var artifacts = new List<string>();
        var boutiques = new List<BoutiqueManifest>();

        try
        {
            _logger.LogInformation("Starting build pipeline");

            var solutionRoot = FindSolutionRoot(options.WorkingDirectory);
            var outputDirectory = options.OutputDirectory
                ?? Path.Combine(solutionRoot, "src", "Atelier", "Atelier.Build", ".artifacts");

            var sharedOutputDir = Path.Combine(outputDirectory, "assemblies");

            var boutiqueDefinitions = await _discoveryService.DiscoverBoutiquesAsync(
                solutionRoot,
                cancellationToken).ConfigureAwait(false);

            if (boutiqueDefinitions.Count == 0)
            {
                _logger.LogWarning("No boutiques found");
                return BuildResult.Failure("No boutiques found");
            }

            _logger.LogInformation("Found {Count} boutiques", boutiqueDefinitions.Count);

            var dependencyGraph = await _dependencyAnalysisService.BuildDependencyGraphAsync(
                boutiqueDefinitions,
                cancellationToken).ConfigureAwait(false);

            var buildOrder = dependencyGraph.TopologicalSort();

            _logger.LogInformation(
                "Build order: {BuildOrder}",
                string.Join(" → ", buildOrder.Select(b => b.Name)));

            if (options.DryRun)
            {
                _logger.LogInformation("Dry run - skipping actual build");
                return BuildResult.Success(artifacts, boutiques);
            }

            foreach (var definition in buildOrder)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var manifest = await _compilationService.CompileBoutiqueAsync(
                    definition,
                    sharedOutputDir,
                    cancellationToken).ConfigureAwait(false);

                _logger.LogInformation("Built boutique: {Name}", definition.Name);

                var requiredAssemblies = await _dependencyAnalysisService.AnalyzeRequisitesAsync(
                    manifest.OutputAssembly,
                    sharedOutputDir,
                    cancellationToken).ConfigureAwait(false);

                manifest = manifest with { RequisiteAssemblies = requiredAssemblies.ToNameList() };

                boutiques.Add(manifest);
                artifacts.Add(manifest.OutputAssembly);
            }

            var manifestPath = await _artifactManagementService.GenerateRequisiteManifestAsync(
                boutiques,
                outputDirectory,
                cancellationToken).ConfigureAwait(false);

            artifacts.Add(manifestPath);

            var assemblyLoaderPath = await _artifactManagementService.GenerateAssemblyLoaderAsync(
                boutiques,
                outputDirectory,
                sharedOutputDir,
                cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(assemblyLoaderPath))
            {
                artifacts.Add(assemblyLoaderPath);
            }

            if (options.GenerateDiagram)
            {
                var diagramDir = Path.Combine(outputDirectory, "diagrams");
                var diagramPath = await _generationService.GenerateDiagramAsync(
                    boutiques,
                    diagramDir,
                    cancellationToken).ConfigureAwait(false);

                artifacts.Add(diagramPath);
            }

            await _artifactManagementService.EnforceRetentionPolicyAsync(
                outputDirectory,
                maxRetainedBuilds: 5,
                cancellationToken).ConfigureAwait(false);

            return BuildResult.Success(artifacts, boutiques);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Build failed: {Message}", ex.Message);
            return BuildResult.Failure(ex.Message);
        }
    }

    public async Task<BuildResult> ExecuteGenerationAsync(
        GenerationOptions options,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await ExecuteGenerationCoreAsync(options, cancellationToken).ConfigureAwait(false);
        result = result with { Duration = stopwatch.Elapsed };

        if (result.IsSuccess)
        {
            _logger.LogInformation(
                "Generation complete: {Artifacts} artifacts in {Duration:F2}s",
                result.GeneratedArtifacts.Count,
                result.Duration.TotalSeconds);
        }

        return result;
    }

    private async Task<BuildResult> ExecuteGenerationCoreAsync(
        GenerationOptions options,
        CancellationToken cancellationToken)
    {
        var artifacts = new List<string>();

        try
        {
            _logger.LogInformation("Starting boutique generation");

            var solutionRoot = FindSolutionRoot(options.WorkingDirectory);
            var boutiquesDir = Path.Combine(solutionRoot, options.BoutiquesDirectory);

            if (!Directory.Exists(boutiquesDir))
            {
                _logger.LogWarning("Boutiques directory not found: {Directory}", boutiquesDir);
                return BuildResult.Success(artifacts, []);
            }

            var boutiqueYamlFiles = Directory.GetFiles(
                boutiquesDir,
                "boutique.yml",
                SearchOption.AllDirectories);

            if (boutiqueYamlFiles.Length == 0)
            {
                _logger.LogWarning("No boutique.yml files found");
                return BuildResult.Success(artifacts, []);
            }

            _logger.LogInformation("Found {Count} boutique YAML files", boutiqueYamlFiles.Length);

            var compiledAssembliesDir = Path.Combine(
                solutionRoot,
                "src",
                "Atelier",
                "Atelier.Build",
                ".artifacts",
                "assemblies");

            var schemas = new List<BoutiqueYamlSchema>();

            foreach (var yamlPath in boutiqueYamlFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var boutiqueDir = Path.GetDirectoryName(yamlPath)!;
                var boutiqueName = Path.GetFileName(boutiqueDir);

                _logger.LogInformation("Processing boutique: {Name}", boutiqueName);

                var schema = await _discoveryService.ParseYamlSchemaAsync(yamlPath, cancellationToken).ConfigureAwait(false);
                schemas.Add(schema);

                if (options.DryRun)
                {
                    _logger.LogInformation("Dry run - skipping artifact generation for {Name}", boutiqueName);
                    continue;
                }

                var dependencyGraph = await _dependencyAnalysisService.AnalyzeProductDependenciesAsync(
                    schema.Products?.Where(p => !string.IsNullOrEmpty(p.Assembly))
                        .Select(p => (p.Type, p.Assembly!)) ?? [],
                    compiledAssembliesDir,
                    cancellationToken).ConfigureAwait(false);

                var generated = await _generationService.GenerateBoutiqueProjectAsync(
                    schema,
                    dependencyGraph,
                    boutiqueDir,
                    solutionRoot,
                    compiledAssembliesDir,
                    cancellationToken).ConfigureAwait(false);

                artifacts.AddRange(generated.AllPaths);

                _logger.LogInformation(
                    "Generated {Count} artifacts for {Name}",
                    generated.AllPaths.Count,
                    boutiqueName);
            }

            if (!options.DryRun)
            {
                var dockerComposePath = Path.Combine(solutionRoot, "docker-compose.yml");
                var composePath = await _generationService.GenerateDockerComposeAsync(
                    schemas,
                    dockerComposePath,
                    cancellationToken).ConfigureAwait(false);

                artifacts.Add(composePath);
            }

            return BuildResult.Success(artifacts, []);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Generation failed: {Message}", ex.Message);
            return BuildResult.Failure(ex.Message);
        }
    }

    public async Task<BuildResult> ExecuteDirectBuildAsync(
        BuildOptions options,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await ExecuteDirectBuildCoreAsync(options, cancellationToken).ConfigureAwait(false);
        result = result with { Duration = stopwatch.Elapsed };

        if (result.IsSuccess)
        {
            _logger.LogInformation("Build complete in {Duration:F2}s", result.Duration.TotalSeconds);
        }

        return result;
    }

    private async Task<BuildResult> ExecuteDirectBuildCoreAsync(
        BuildOptions options,
        CancellationToken cancellationToken)
    {
        var artifacts = new List<string>();

        try
        {
            if (string.IsNullOrEmpty(options.ProjectPath))
            {
                throw new ArgumentException("ProjectPath is required for direct build");
            }

            _logger.LogInformation("Starting direct build: {Path}", options.ProjectPath);

            var fullPath = Path.IsPathRooted(options.ProjectPath)
                ? options.ProjectPath
                : Path.Combine(options.WorkingDirectory, options.ProjectPath);

            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"File not found: {fullPath}");
            }

            var isSolution = fullPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase);
            var isProject = fullPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);

            if (!isSolution && !isProject)
            {
                throw new ArgumentException("Path must be a .sln or .csproj file");
            }

            if (options.DryRun)
            {
                _logger.LogInformation("Dry run - would build: {Path}", fullPath);
                return BuildResult.Success(artifacts, []);
            }

            var outputDirectory = options.OutputDirectory
                ?? Path.Combine(Path.GetDirectoryName(fullPath)!, "bin", options.Configuration);

            bool success;

            if (isSolution)
            {
                success = await _compilationService.CompileSolutionAsync(
                    fullPath,
                    outputDirectory,
                    options.Configuration,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                success = await _compilationService.CompileProjectAsync(
                    fullPath,
                    outputDirectory,
                    options.Configuration,
                    cancellationToken).ConfigureAwait(false);
            }

            if (!success)
            {
                return BuildResult.Failure("Build failed");
            }

            var name = Path.GetFileNameWithoutExtension(fullPath);
            var outputAssembly = Path.Combine(outputDirectory, $"{name}.dll");

            if (File.Exists(outputAssembly))
            {
                artifacts.Add(outputAssembly);
            }

            return BuildResult.Success(artifacts, []);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Direct build failed: {Message}", ex.Message);
            return BuildResult.Failure(ex.Message);
        }
    }

    private static string FindSolutionRoot(string workingDirectory)
    {
        var currentDir = workingDirectory;

        while (!string.IsNullOrEmpty(currentDir))
        {
            if (Directory.GetFiles(currentDir, "*.sln").Length > 0)
            {
                return currentDir;
            }

            currentDir = Path.GetDirectoryName(currentDir);
        }

        return workingDirectory;
    }
}
