namespace Atelier.Build.Discovery;

public class ProductDefinition
{
        public required string Name { get; init; }

        public required string Version { get; init; }

        public string? Description { get; init; }

        public required string SourceDirectory { get; init; }

        public string? ProductName { get; init; }

        public IReadOnlyList<string> Dependencies { get; init; } = [];

        public IReadOnlyList<string> ProjectReferences { get; init; } = [];

        public string? SolutionPath { get; init; }

        public ProductBuildSettings Build { get; init; } = new();
}

public class ProductBuildSettings
{
        public string Configuration { get; init; } = "Release";

        public bool TreatWarningsAsErrors { get; init; } = false;

        public IReadOnlyList<string> AdditionalMsBuildArgs { get; init; } = [];
}
