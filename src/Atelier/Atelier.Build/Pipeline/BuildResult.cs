namespace Atelier.Build.Pipeline;

public record BuildResult
{
    public bool IsSuccess { get; init; }
    public string? Error { get; init; }
    public TimeSpan Duration { get; init; }
    public IReadOnlyList<string> GeneratedArtifacts { get; init; } = [];
    public IReadOnlyList<BoutiqueManifest> BuiltBoutiques { get; init; } = [];

    public static BuildResult Success(IReadOnlyList<string> artifacts, IReadOnlyList<BoutiqueManifest> boutiques)
        => new()
        {
            IsSuccess = true,
            GeneratedArtifacts = artifacts,
            BuiltBoutiques = boutiques
        };

    public static BuildResult Failure(string error)
        => new()
        {
            IsSuccess = false,
            Error = error
        };
}

public record BoutiqueManifest
{
    public required string Name { get; init; }
    public required string ProjectPath { get; init; }
    public required string OutputAssembly { get; init; }
    public IReadOnlyList<string> Offerings { get; init; } = [];
    public IReadOnlyList<string> Dependencies { get; init; } = [];
    public IReadOnlyList<string> RequisiteAssemblies { get; init; } = [];
}
