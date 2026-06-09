using Atelier.Build.Discovery;
using Spectre.Console;

namespace Atelier.Build.Pipeline;

public sealed class BuildPresenter
{
    public void PipelineHeader()
    {
        AnsiConsole.Write(new Rule("[blue]SMASH Build Pipeline[/]").RuleStyle("dim"));
        AnsiConsole.WriteLine();
    }

    public void FullBuildSummary(BuildResult result)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[green]Build Complete[/]").RuleStyle("dim"));

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Metric")
            .AddColumn("Value");

        table.AddRow("Duration", $"{result.Duration.TotalSeconds:F2}s");
        table.AddRow("Boutiques", result.BuiltBoutiques.Count.ToString());
        table.AddRow("Total Assemblies", result.BuiltBoutiques.Sum(b => b.RequisiteAssemblies.Count).ToString());
        table.AddRow("Artifacts", result.GeneratedArtifacts.Count.ToString());

        AnsiConsole.Write(table);
    }

    public void PipelineFailed(string message)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[red]✗ Build failed: {message}[/]");
    }

    public void DiscoveringBoutiques()
    {
        AnsiConsole.MarkupLine("[yellow]Phase 1:[/] Discovering boutiques...");
    }

    public void NoBoutiques()
    {
        AnsiConsole.MarkupLine("[red]  ✗ No boutiques found[/]");
    }

    public void FoundBoutiques(int count)
    {
        AnsiConsole.MarkupLine($"[green]  ✓[/] Found {count} boutique(s)");
    }

    public void BuildOrder(IReadOnlyList<BoutiqueDefinition> buildOrder)
    {
        AnsiConsole.MarkupLine($"[dim]    Build order: {string.Join(" → ", buildOrder.Select(b => b.Name))}[/]");
        AnsiConsole.WriteLine();
    }

    public void BuildingSolution()
    {
        AnsiConsole.MarkupLine("[yellow]Phase 2:[/] Building solution...");
    }

    public void BuiltBoutique(string name, int requisiteCount)
    {
        AnsiConsole.MarkupLine($"[green]  ✓[/] Built {name}");
        AnsiConsole.MarkupLine($"[dim]    → {requisiteCount} requisite assemblies[/]");
    }

    public void GeneratingArtifacts()
    {
        AnsiConsole.MarkupLine("[yellow]Phase 3:[/] Generating artifacts...");
    }

    public void RequisiteManifest(string fileName)
    {
        AnsiConsole.MarkupLine($"[green]  ✓[/] Requisite manifest: [dim]{fileName}[/]");
    }

    public void AssemblyLoader(string fileName)
    {
        AnsiConsole.MarkupLine($"[green]  ✓[/] Assembly loader: [dim]{fileName}[/]");
    }

    public void MermaidDiagram(string fileName)
    {
        AnsiConsole.MarkupLine($"[green]  ✓[/] Mermaid diagram: [dim]{fileName}[/]");
    }

    public void DryRunPlan(IReadOnlyList<BoutiqueDefinition> buildOrder, bool generateDiagram)
    {
        AnsiConsole.MarkupLine("[yellow]Dry run - would execute:[/]");

        foreach (var definition in buildOrder)
        {
            AnsiConsole.MarkupLine($"  [dim]→[/] Build {definition.Name}");
        }

        if (generateDiagram)
        {
            AnsiConsole.MarkupLine("  [dim]→[/] Generate mermaid diagram");
        }
    }

    public void SubsystemHeader(string subsystemName)
    {
        AnsiConsole.Write(new Rule($"[blue]Subsystem Build: {subsystemName}[/]").RuleStyle("dim"));
        AnsiConsole.WriteLine();
    }

    public void Phase(int phaseNumber, string title)
    {
        AnsiConsole.MarkupLine($"[yellow]Phase {phaseNumber}:[/] {title}");
    }

    public void SubsystemFound(SubsystemDefinition subsystem)
    {
        AnsiConsole.MarkupLine($"[green]  ✓[/] Found subsystem: {subsystem.Description ?? subsystem.Name}");
        AnsiConsole.MarkupLine($"[dim]    Directory: {subsystem.Directory}[/]");

        if (subsystem.SolutionPath != null)
        {
            AnsiConsole.MarkupLine($"[dim]    Solution: {Path.GetFileName(subsystem.SolutionPath)}[/]");
        }

        if (subsystem.Dependencies.Count > 0)
        {
            AnsiConsole.MarkupLine($"[dim]    Dependencies: {string.Join(", ", subsystem.Dependencies)}[/]");
        }

        AnsiConsole.WriteLine();
    }

    public void SubsystemDryRun(SubsystemDefinition subsystem,
                                string platform,
                                IReadOnlyList<PreBuildStep>? preBuildSteps,
                                bool runTests)
    {
        AnsiConsole.MarkupLine("[yellow]Dry run - would execute:[/]");

        if (subsystem.PreBuild != null)
        {
            if (preBuildSteps != null && preBuildSteps.Count > 0)
            {
                AnsiConsole.MarkupLine($"  [cyan]Pre-build ({platform}):[/]");
                foreach (var step in preBuildSteps)
                {
                    AnsiConsole.MarkupLine($"  [dim]→[/] {step.Name}: {step.Command}");
                    if (!string.IsNullOrWhiteSpace(step.WorkingDirectory))
                    {
                        AnsiConsole.MarkupLine($"    [dim]in {step.WorkingDirectory}[/]");
                    }
                }
            }
            else
            {
                AnsiConsole.MarkupLine($"  [dim]No pre-build steps for {platform}[/]");
            }
        }

        if (subsystem.SolutionPath != null)
        {
            AnsiConsole.MarkupLine($"  [dim]→[/] dotnet build {Path.GetFileName(subsystem.SolutionPath)} -c {subsystem.BuildConfiguration}");
        }
        if (runTests && subsystem.TestProjects.Count > 0)
        {
            foreach (var testProject in subsystem.TestProjects)
            {
                AnsiConsole.MarkupLine($"  [dim]→[/] dotnet test {testProject}");
            }
        }
    }

    public void DependencyUpToDate(string depName)
    {
        AnsiConsole.MarkupLine($"[dim]  ✓ {depName} (up-to-date)[/]");
    }

    public void DependencyBuilt(string depName)
    {
        AnsiConsole.MarkupLine($"[green]  ✓[/] Built dependency: {depName}");
    }

    public void SubsystemUpToDate(string name, double elapsedSeconds)
    {
        AnsiConsole.MarkupLine($"[green]✓ {name} is up-to-date (incremental)[/]");
        AnsiConsole.MarkupLine($"[dim]Build completed in {elapsedSeconds:F2}s[/]");
    }

    public void NoSolutionWarning()
    {
        AnsiConsole.MarkupLine("[yellow]  Warning: No solution file found[/]");
    }

    public void SubsystemBuilt(string name)
    {
        AnsiConsole.MarkupLine($"[green]  ✓[/] Built {name}");
    }

    public void TestPassed(string testProject)
    {
        AnsiConsole.MarkupLine($"[green]  ✓[/] {testProject}");
    }

    public void TestFailed(string testProject)
    {
        AnsiConsole.MarkupLine($"[red]  ✗[/] {testProject}");
    }

    public void TestNotFound(string testProject)
    {
        AnsiConsole.MarkupLine($"[yellow]  ⚠[/] {testProject} (not found)");
    }

    public void BenchmarkPassed(string project, string outputPath)
    {
        AnsiConsole.MarkupLine($"[green]  ✓[/] {project}");
        AnsiConsole.MarkupLine($"[dim]  Results: {outputPath}[/]");
    }

    public void BenchmarkFailed(string project)
    {
        AnsiConsole.MarkupLine($"[red]  ✗[/] {project} failed");
    }

    public void BenchmarkNotFound(string project)
    {
        AnsiConsole.MarkupLine($"[yellow]  ⚠[/] {project} (not found)");
    }

    public void Newline()
    {
        AnsiConsole.WriteLine();
    }

    public void SubsystemSummary(SubsystemDefinition subsystem, double elapsedSeconds, bool runTests)
    {
        AnsiConsole.Write(new Rule("[green]Build Complete[/]").RuleStyle("dim"));

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Metric")
            .AddColumn("Value");

        table.AddRow("Subsystem", subsystem.Name);
        table.AddRow("Duration", $"{elapsedSeconds:F2}s");
        if (subsystem.Dependencies.Count > 0)
        {
            table.AddRow("Dependencies", subsystem.Dependencies.Count.ToString());
        }
        if (runTests)
        {
            table.AddRow("Test Projects", subsystem.TestProjects.Count.ToString());
        }

        AnsiConsole.Write(table);
    }

    public void GenerationHeader()
    {
        AnsiConsole.Write(new Rule("[blue]Per-Boutique Project Generation[/]").RuleStyle("dim"));
        AnsiConsole.WriteLine();
    }

    public void CreatedBoutiquesDirectory(string directory)
    {
        AnsiConsole.MarkupLine($"[dim]Created boutiques directory: {directory}[/]");
    }

    public void NoBoutiquesToGenerate()
    {
        AnsiConsole.MarkupLine("[yellow]  No boutiques found[/]");
        AnsiConsole.MarkupLine("[dim]  Create src/{subsystem}/boutique.yml to get started[/]");
    }

    public void FoundBoutiquesWithPorts(IReadOnlyList<BoutiqueDefinition> definitions)
    {
        AnsiConsole.MarkupLine($"[green]  ✓[/] Found {definitions.Count} boutique(s)");
        foreach (var def in definitions)
        {
            AnsiConsole.MarkupLine($"[dim]    • {def.Name} (ports: {def.Ports.Http}/{def.Ports.Grpc}/{def.Ports.Metrics})[/]");
        }
    }

    public void AssembliesNotCompiledWarning()
    {
        AnsiConsole.MarkupLine("[yellow]  Warning: Assemblies not compiled yet. Run 'smash' first.[/]");
        AnsiConsole.MarkupLine("[dim]  Generating boutique projects with limited dependency analysis...[/]");
    }

    public void GeneratingBoutiqueProjects()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Phase 2:[/] Generating boutique projects...");
    }

    public void ProcessingBoutique(string boutiqueName)
    {
        AnsiConsole.MarkupLine($"[dim]  Processing: {boutiqueName}[/]");
    }

    public void BoutiqueDryRun(string outputDir, string pascalName)
    {
        AnsiConsole.MarkupLine("[yellow]    Dry run - would generate:[/]");
        AnsiConsole.MarkupLine($"[dim]      • {outputDir}/Program.g.cs[/]");
        AnsiConsole.MarkupLine($"[dim]      • {outputDir}/Atelier.Host.{pascalName}.csproj[/]");
        AnsiConsole.MarkupLine($"[dim]      • {outputDir}/Dockerfile[/]");
    }

    public void GeneratedArtifact(string fileName)
    {
        AnsiConsole.MarkupLine($"[green]    ✓[/] {fileName}");
    }

    public void GeneratedProgram()
    {
        AnsiConsole.MarkupLine("[green]    ✓[/] Program.g.cs");
    }

    public void GeneratedStandaloneCompose(string fileName)
    {
        AnsiConsole.MarkupLine($"[green]    ✓[/] {fileName} (Standalone)");
    }

    public void BoutiqueDependencyStats(int assemblyCount, int typeCount)
    {
        AnsiConsole.MarkupLine($"[dim]      {assemblyCount} assemblies, {typeCount} types[/]");
    }

    public void BoutiqueGenerationError(string message, Exception ex, bool verbose)
    {
        AnsiConsole.MarkupLine($"[red]    ✗ Error: {message}[/]");
        if (verbose)
        {
            AnsiConsole.WriteException(ex);
        }
    }

    public void GeneratingCompose()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Phase 3:[/] Generating docker-compose.yml...");
    }

    public void GeneratedCompose(int schemaCount)
    {
        AnsiConsole.MarkupLine("[green]  ✓[/] docker-compose.yml");
        AnsiConsole.MarkupLine($"[dim]    Orchestration for {schemaCount} boutique(s) + infrastructure[/]");
    }

    public void ComposeWarning(string message)
    {
        AnsiConsole.MarkupLine($"[yellow]  Warning: Could not generate docker-compose.yml: {message}[/]");
    }

    public void NetworkTopologyViolations(IReadOnlyList<Generation.NetworkTopologyViolation> violations)
    {
        AnsiConsole.MarkupLine($"[red]✗ Network topology validation failed ({violations.Count}):[/]");
        foreach (var violation in violations)
        {
            AnsiConsole.MarkupLine($"[red]    {violation.Kind}: {Markup.Escape(violation.Detail)}[/]");
        }
    }

    public void GeneratingBenchmarkContainers()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Phase 4:[/] Generating benchmark containers...");
    }

    public void FoundBenchmarkProjects(int count)
    {
        AnsiConsole.MarkupLine($"[green]  ✓[/] Found {count} benchmark project(s)");
    }

    public void GeneratedBenchmarkDockerfile(string fileName)
    {
        AnsiConsole.MarkupLine($"[green]    ✓[/] {fileName}");
    }

    public void GeneratedBenchmarkCompose(int count)
    {
        AnsiConsole.MarkupLine("[green]  ✓[/] docker-compose.benchmarks.yml");
        AnsiConsole.MarkupLine($"[dim]    {count} containerized benchmark suites[/]");
    }

    public void NoBenchmarkProjects()
    {
        AnsiConsole.MarkupLine("[dim]  No benchmark projects found (add 'benchmark: project:' to smash.yml)[/]");
    }

    public void BenchmarkContainersWarning(string message)
    {
        AnsiConsole.MarkupLine($"[yellow]  Warning: Could not generate benchmark containers: {message}[/]");
    }

    public void GeneratingTestContainers()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Phase 5:[/] Generating test containers...");
    }

    public void FoundTestSuites(int suiteCount, int totalTestProjects)
    {
        AnsiConsole.MarkupLine($"[green]  ✓[/] Found {suiteCount} test suite(s)");
        AnsiConsole.MarkupLine($"[dim]    {totalTestProjects} test projects across {suiteCount} subsystems[/]");
    }

    public void GeneratedTestDockerfile(string fileName, int projectCount)
    {
        AnsiConsole.MarkupLine($"[green]    ✓[/] {fileName} ({projectCount} projects)");
    }

    public void GeneratedTestCompose(int count)
    {
        AnsiConsole.MarkupLine("[green]  ✓[/] docker-compose.tests.yml");
        AnsiConsole.MarkupLine($"[dim]    {count} containerized test suites[/]");
    }

    public void NoTestProjects()
    {
        AnsiConsole.MarkupLine("[dim]  No test projects found (add 'test: projects:' to smash.yml)[/]");
    }

    public void TestContainersWarning(string message)
    {
        AnsiConsole.MarkupLine($"[yellow]  Warning: Could not generate test containers: {message}[/]");
    }

    public void GenerationSummary(int boutiqueCount, int artifactCount, double elapsedSeconds)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[green]Generation Complete[/]").RuleStyle("dim"));

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Metric")
            .AddColumn("Value");

        table.AddRow("Duration", $"{elapsedSeconds:F2}s");
        table.AddRow("Boutiques", boutiqueCount.ToString());
        table.AddRow("Artifacts", artifactCount.ToString());

        AnsiConsole.Write(table);
    }
}
