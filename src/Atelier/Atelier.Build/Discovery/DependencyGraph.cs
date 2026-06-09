namespace Atelier.Build.Discovery;

public class DependencyGraph
{
    private readonly Dictionary<string, BoutiqueDefinition> _nodes = new();
    private readonly Dictionary<string, HashSet<string>> _edges = new();

    public void AddNode(BoutiqueDefinition definition)
    {
        _nodes[definition.Name] = definition;
        if (!_edges.ContainsKey(definition.Name))
        {
            _edges[definition.Name] = [];
        }
    }

    public void AddEdge(string from, string to)
    {
        if (!_edges.ContainsKey(from))
        {
            _edges[from] = [];
        }

        _edges[from].Add(to);
    }

    public IReadOnlyList<BoutiqueDefinition> TopologicalSort()
    {
        var visited = new HashSet<string>();
        var recursionStack = new HashSet<string>();
        var result = new Stack<BoutiqueDefinition>();

        foreach (var node in _nodes.Keys.OrderBy(n => n, StringComparer.Ordinal))
        {
            if (!visited.Contains(node))
            {
                var cycleNode = TopologicalSortVisit(node,
                                                     visited,
                                                     recursionStack,
                                                     result);
                if (cycleNode is not null)
                {
                    throw new InvalidOperationException($"Circular dependency detected involving {cycleNode}");
                }
            }
        }

        return result.ToList();
    }

    private string? TopologicalSortVisit(
        string root,
        HashSet<string> visited,
        HashSet<string> recursionStack,
        Stack<BoutiqueDefinition> result)
    {
        var work = new Stack<(string Node, IEnumerator<string> Children)>();

        visited.Add(root);
        recursionStack.Add(root);
        work.Push((root, EnumerateDependencies(root)));

        while (work.Count > 0)
        {
            var frame = work.Peek();

            if (frame.Children.MoveNext())
            {
                var dependency = frame.Children.Current;

                if (recursionStack.Contains(dependency))
                {
                    return dependency;
                }

                if (!visited.Contains(dependency))
                {
                    visited.Add(dependency);
                    recursionStack.Add(dependency);
                    work.Push((dependency, EnumerateDependencies(dependency)));
                }
            }
            else
            {
                work.Pop();
                recursionStack.Remove(frame.Node);
                result.Push(_nodes[frame.Node]);
            }
        }

        return null;
    }

    private IEnumerator<string> EnumerateDependencies(string node)
    {
        if (!_edges.TryGetValue(node, out var dependencies))
        {
            yield break;
        }

        foreach (var dependency in dependencies.OrderBy(d => d, StringComparer.Ordinal))
        {
            if (_nodes.ContainsKey(dependency))
            {
                yield return dependency;
            }
        }
    }

    public IReadOnlyList<string> GetDependencies(string boutiqueName)
    {
        return _edges.TryGetValue(boutiqueName, out var deps)
            ? deps.OrderBy(d => d, StringComparer.Ordinal).ToList()
            : [];
    }
}

public class DependencyGraphBuilder
{
    public DependencyGraph Build(IReadOnlyList<BoutiqueDefinition> definitions)
    {
        var graph = new DependencyGraph();

        foreach (var definition in definitions)
        {
            graph.AddNode(definition);
        }

        foreach (var definition in definitions)
        {
            foreach (var dependency in definition.Dependencies)
            {
                graph.AddEdge(definition.Name, dependency);
            }
        }

        return graph;
    }
}
