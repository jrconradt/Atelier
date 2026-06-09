namespace Atelier.Build.Commands;

public record DependencyTreeNode
{
        public required string Name { get; init; }

        public int DependencyCount { get; init; }

        public int DependentCount { get; init; }

        public List<DependencyTreeNode> Children { get; init; } = new();
}
