using System.Text.Json;
using Atelier.Build.Analysis;
using Atelier.Build.Discovery;
using Atelier.Build.Generation;
using Spectre.Console;

namespace Atelier.Build.Pipeline;

public sealed class FullPipelineRunner
{
    private readonly BuildContext _context;
    private readonly BuildPresenter _presenter;
    private readonly ShellRunner _shell;

    public FullPipelineRunner(BuildContext context, BuildPresenter presenter, ShellRunner shell)
    {
        _context = context;
        _presenter = presenter;
        _shell = shell;
    }

    public async Task<BuildResult> ExecuteAsync(
        List<string> artifacts,
        List<BoutiqueManifest> boutiques)
    {
        _presenter.DiscoveringBoutiques();
        var boutiqueDefinitions = await DiscoverBoutiquesAsync().ConfigureAwait(false);

        if (boutiqueDefinitions.Count == 0)
        {
            _presenter.NoBoutiques();
            return BuildResult.Failure("No boutiques found");
        }

        _presenter.FoundBoutiques(boutiqueDefinitions.Count);

        var dependencyGraph = BuildDependencyGraph(boutiqueDefinitions);
        var buildOrder = ResolveBuildOrder(dependencyGraph);

        _presenter.BuildOrder(buildOrder);

        if (_context.DryRun)
        {
            _presenter.DryRunPlan(buildOrder, _context.GenerateDiagram);
            return BuildResult.Success(artifacts, boutiques);
        }

        var generationRunner = new BoutiqueGenerationRunner(_context, _presenter);
        var generationResult = await generationRunner.ExecuteAsync(artifacts).ConfigureAwait(false);
        if (!generationResult.IsSuccess)
        {
            return generationResult;
        }

        var sharedOutputDir = Path.Combine(_context.BuildOutputDirectory, "assemblies");
        await BuildBoutiquesAsync(buildOrder, sharedOutputDir, artifacts, boutiques).ConfigureAwait(false);

        if (_context.RunTests)
        {
            AnsiConsole.MarkupLine("[yellow]Phase 2b:[/] Running generated tests...");

            var harness = new GeneratedTestHarness(_context);
            var testOutcome = await harness.RunAsync(new GeneratedTestOptions(DryRun: false,
                                                                              Filter: null,
                                                                              MaxNeedsFixture: 0,
                                                                              AllowlistPath: null)).ConfigureAwait(false);
            if (testOutcome.ExitCode != 0)
            {
                AnsiConsole.MarkupLine("[red]  ✗[/] Generated tests failed");
                return BuildResult.Failure($"Generated tests failed: {testOutcome.Fail} failing, {testOutcome.BudgetBreaches} budget breaches");
            }

            AnsiConsole.MarkupLine("[green]  ✓[/] Generated tests passed");
            _presenter.Newline();
        }

        await GenerateFullBuildArtifactsAsync(boutiques, sharedOutputDir, artifacts).ConfigureAwait(false);

        await ManageArtifactRetentionAsync().ConfigureAwait(false);

        return BuildResult.Success(artifacts, boutiques);
    }

    private async Task BuildBoutiquesAsync(
        IReadOnlyList<BoutiqueDefinition> buildOrder,
        string sharedOutputDir,
        List<string> artifacts,
        List<BoutiqueManifest> boutiques)
    {
        _presenter.BuildingSolution();

        foreach (var definition in buildOrder)
        {
            var manifest = await BuildBoutiqueAsync(definition).ConfigureAwait(false);

            var analyzer = new RequisiteAnalyzer(_context);
            var requiredAssemblies = analyzer.AnalyzeRequiredAssemblies(sharedOutputDir, manifest.OutputAssembly);

            manifest = manifest with { RequisiteAssemblies = requiredAssemblies.ToNameList() };

            _presenter.BuiltBoutique(definition.Name, requiredAssemblies.Count);

            boutiques.Add(manifest);
            artifacts.Add(manifest.OutputAssembly);
        }

        _presenter.Newline();
    }

    private async Task GenerateFullBuildArtifactsAsync(
        IReadOnlyList<BoutiqueManifest> boutiques,
        string sharedOutputDir,
        List<string> artifacts)
    {
        _presenter.GeneratingArtifacts();

        var manifestPath = await GenerateRequisiteManifestAsync(boutiques).ConfigureAwait(false);
        artifacts.Add(manifestPath);
        _presenter.RequisiteManifest(Path.GetFileName(manifestPath));

        var assemblyLoaderPath = await GenerateAssemblyLoaderAsync(boutiques, sharedOutputDir).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(assemblyLoaderPath))
        {
            artifacts.Add(assemblyLoaderPath);
            _presenter.AssemblyLoader(Path.GetFileName(assemblyLoaderPath));
        }

        if (_context.GenerateDiagram)
        {
            var diagramPath = await GenerateDiagramAsync(boutiques).ConfigureAwait(false);
            artifacts.Add(diagramPath);
            _presenter.MermaidDiagram(Path.GetFileName(diagramPath));
        }
    }

    private async Task<IReadOnlyList<BoutiqueDefinition>> DiscoverBoutiquesAsync()
    {
        var discoverer = new BoutiqueDiscoverer(_context);
        return await discoverer.DiscoverAsync().ConfigureAwait(false);
    }

    private DependencyGraph BuildDependencyGraph(IReadOnlyList<BoutiqueDefinition> definitions)
    {
        var graphBuilder = new DependencyGraphBuilder();
        return graphBuilder.Build(definitions);
    }

    private IReadOnlyList<BoutiqueDefinition> ResolveBuildOrder(DependencyGraph graph)
    {
        return graph.TopologicalSort();
    }

    private async Task<BoutiqueManifest> BuildBoutiqueAsync(BoutiqueDefinition definition)
    {
        LogPhase($"Building {definition.Name}");

        var compiler = new BoutiqueCompiler(_context);
        return await compiler.CompileAsync(definition).ConfigureAwait(false);
    }

    private async Task<string> GenerateDiagramAsync(IReadOnlyList<BoutiqueManifest> boutiques)
    {
        LogPhase("Generating service interaction diagram");

        var generator = new MermaidDiagramGenerator(_context);
        return await generator.GenerateAsync(boutiques).ConfigureAwait(false);
    }

    private Task<string> GenerateAssemblyLoaderAsync(
        IReadOnlyList<BoutiqueManifest> boutiques,
        string outputDirectory)
    {
        return Task.FromResult(string.Empty);
    }

    private async Task<string> GenerateRequisiteManifestAsync(IReadOnlyList<BoutiqueManifest> boutiques)
    {
        LogPhase("Generating requisite manifest");

        var manifest = boutiques.ToDictionary(
            b => b.Name,
            b => new
            {
                b.OutputAssembly,
                b.Offerings,
                b.Dependencies,
                b.RequisiteAssemblies
            });

        var outputPath = Path.Combine(_context.BuildOutputDirectory, "requisite-manifest.json");

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var json = JsonSerializer.Serialize(manifest, options);
        await File.WriteAllTextAsync(outputPath, json).ConfigureAwait(false);

        return outputPath;
    }

    private async Task ManageArtifactRetentionAsync()
    {
        var manager = new ArtifactRetentionManager(_context);
        await manager.EnforceRetentionAsync().ConfigureAwait(false);
    }

    private void LogPhase(string message)
    {
        if (_context.Verbose)
        {
            AnsiConsole.MarkupLine($"[blue]▸[/] {message}");
        }
    }
}
