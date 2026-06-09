using Atelier.Build.Pipeline;

namespace Atelier.Build.Services.Abstractions;

public interface IBuildOrchestrationService
{
        public Task<BuildResult> ExecuteBuildAsync(
        BuildOptions options,
        CancellationToken cancellationToken = default);

        public Task<BuildResult> ExecuteGenerationAsync(
        GenerationOptions options,
        CancellationToken cancellationToken = default);

        public Task<BuildResult> ExecuteDirectBuildAsync(
        BuildOptions options,
        CancellationToken cancellationToken = default);
}

public record BuildOptions
{
    public string WorkingDirectory { get; init; } = Directory.GetCurrentDirectory();
    public string? ProjectPath { get; init; }
    public string Configuration { get; init; } = "Debug";
    public bool Verbose { get; init; }
    public bool DryRun { get; init; }
    public bool GenerateDiagram { get; init; }
    public string? OutputDirectory { get; init; }
}

public record GenerationOptions
{
    public string WorkingDirectory { get; init; } = Directory.GetCurrentDirectory();
    public string BoutiquesDirectory { get; init; } = "boutiques";
    public bool Verbose { get; init; }
    public bool DryRun { get; init; }
    public string? OutputDirectory { get; init; }
}
