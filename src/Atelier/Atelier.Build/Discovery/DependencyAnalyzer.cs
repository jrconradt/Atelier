using Atelier.Build.Commands;

namespace Atelier.Build.Discovery;

public class DependencyAnalyzer
{
    private readonly Dictionary<string, SubsystemDefinition> _subsystems;
    private readonly Dictionary<string, HashSet<string>> _dependencies;
    private readonly Dictionary<string, HashSet<string>> _dependents;

    public DependencyAnalyzer(IReadOnlyList<SubsystemDefinition> subsystems)
    {

        _subsystems = subsystems.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
        _dependencies = new(StringComparer.OrdinalIgnoreCase);
        _dependents = new(StringComparer.OrdinalIgnoreCase);

        BuildGraphs();
    }

    private void BuildGraphs()
    {

        foreach (var subsystem in _subsystems.Values)
        {
            _dependencies[subsystem.Name] = new(StringComparer.OrdinalIgnoreCase);
            _dependents[subsystem.Name] = new(StringComparer.OrdinalIgnoreCase);
        }

        foreach (var subsystem in _subsystems.Values)
        {
            foreach (var dep in subsystem.Dependencies)
            {
                if (_subsystems.ContainsKey(dep))
                {
                    _dependencies[subsystem.Name].Add(dep);
                    _dependents[dep].Add(subsystem.Name);
                }
            }
        }
    }

        public HashSet<string> GetDirectDependencies(string subsystemName)
    {
        return _dependencies.TryGetValue(subsystemName, out var deps)
            ? new HashSet<string>(deps, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

        public HashSet<string> GetDirectDependents(string subsystemName)
    {
        return _dependents.TryGetValue(subsystemName, out var deps)
            ? new HashSet<string>(deps, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

        public int GetDirectDependencyCount(string subsystemName)
    {
        return _dependencies.TryGetValue(subsystemName, out var deps)
            ? deps.Count
            : 0;
    }

        public int GetDirectDependentCount(string subsystemName)
    {
        return _dependents.TryGetValue(subsystemName, out var deps)
            ? deps.Count
            : 0;
    }

        public HashSet<string> GetTransitiveDependencies(string subsystemName, int? maxDepth = null)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string node, int depth)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        queue.Enqueue((subsystemName, 0));
        visited.Add(subsystemName);

        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();

            if (maxDepth.HasValue && depth >= maxDepth.Value)
            {
                continue;
            }

            if (_dependencies.TryGetValue(current, out var deps))
            {
                foreach (var dep in deps)
                {
                    if (!visited.Contains(dep))
                    {
                        result.Add(dep);
                        visited.Add(dep);
                        queue.Enqueue((dep, depth + 1));
                    }
                }
            }
        }

        return result;
    }

        public HashSet<string> GetTransitiveDependents(string subsystemName, int? maxDepth = null)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string node, int depth)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        queue.Enqueue((subsystemName, 0));
        visited.Add(subsystemName);

        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();

            if (maxDepth.HasValue && depth >= maxDepth.Value)
            {
                continue;
            }

            if (_dependents.TryGetValue(current, out var deps))
            {
                foreach (var dep in deps)
                {
                    if (!visited.Contains(dep))
                    {
                        result.Add(dep);
                        visited.Add(dep);
                        queue.Enqueue((dep, depth + 1));
                    }
                }
            }
        }

        return result;
    }

        public HashSet<string> GetImpactSet(string subsystemName)
    {
        var impact = GetTransitiveDependents(subsystemName);
        impact.Add(subsystemName);
        return impact;
    }

        public IReadOnlyList<string> GetBuildOrder(IEnumerable<string> subsystemNames)
    {
        var subsystems = subsystemNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recursionStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new Stack<string>();

        foreach (var name in subsystems.OrderBy(n => n, StringComparer.Ordinal))
        {
            if (!visited.Contains(name))
            {
                TopologicalSortVisit(name,
                                     subsystems,
                                     visited,
                                     recursionStack,
                                     result);
            }
        }

        return result.ToList();
    }

    private void TopologicalSortVisit(
        string root,
        HashSet<string> scope,
        HashSet<string> visited,
        HashSet<string> recursionStack,
        Stack<string> result)
    {
        var work = new Stack<(string Node, IEnumerator<string> Children)>();

        visited.Add(root);
        recursionStack.Add(root);
        work.Push((root, EnumerateScopedDependencies(root, scope)));

        while (work.Count > 0)
        {
            var frame = work.Peek();

            if (frame.Children.MoveNext())
            {
                var dep = frame.Children.Current;

                if (recursionStack.Contains(dep))
                {
                    return;
                }

                if (!visited.Contains(dep))
                {
                    visited.Add(dep);
                    recursionStack.Add(dep);
                    work.Push((dep, EnumerateScopedDependencies(dep, scope)));
                }
            }
            else
            {
                work.Pop();
                recursionStack.Remove(frame.Node);
                result.Push(frame.Node);
            }
        }
    }

    private IEnumerator<string> EnumerateScopedDependencies(string node, HashSet<string> scope)
    {
        if (!_dependencies.TryGetValue(node, out var deps))
        {
            yield break;
        }

        foreach (var dep in deps.OrderBy(d => d, StringComparer.Ordinal))
        {
            if (scope.Contains(dep)
                && _subsystems.ContainsKey(dep))
            {
                yield return dep;
            }
        }
    }

        public DependencyTreeNode BuildTree(
        string rootName,
        string direction,
        int? maxDepth,
        HashSet<string>? highlightSet = null)
    {
        if (!_subsystems.ContainsKey(rootName))
        {
            throw new ArgumentException($"Subsystem '{rootName}' not found", nameof(rootName));
        }

        return direction.ToLowerInvariant() switch
        {
            "dependencies" => BuildDependencyTree(rootName, maxDepth, highlightSet),
            "dependents" => BuildDependentTree(rootName, maxDepth, highlightSet),
            "both" => BuildBothTree(rootName, maxDepth, highlightSet),
            _ => throw new ArgumentException($"Invalid direction: {direction}. Must be 'dependencies', 'dependents', or 'both'.", nameof(direction))
        };
    }

    private DependencyTreeNode BuildDependencyTree(
        string rootName,
        int? maxDepth,
        HashSet<string>? highlightSet)
    {
        return BuildTreeIterative(rootName, maxDepth, GetDirectDependencies);
    }

    private DependencyTreeNode BuildDependentTree(
        string rootName,
        int? maxDepth,
        HashSet<string>? highlightSet)
    {
        return BuildTreeIterative(rootName, maxDepth, GetDirectDependents);
    }

    private DependencyTreeNode CreateTreeNode(string name)
    {
        return new DependencyTreeNode
        {
            Name = name,
            DependencyCount = GetDirectDependencyCount(name),
            DependentCount = GetDirectDependentCount(name),
            Children = new()
        };
    }

    private DependencyTreeNode BuildTreeIterative(
        string rootName,
        int? maxDepth,
        Func<string, HashSet<string>> childAccessor)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var work = new Stack<(DependencyTreeNode Node, int Depth, IEnumerator<string> Children)>();

        var root = CreateTreeNode(rootName);
        visited.Add(rootName);
        work.Push((root, 0, EnumerateChildren(rootName, 0, maxDepth, childAccessor)));

        while (work.Count > 0)
        {
            var frame = work.Peek();

            if (frame.Children.MoveNext())
            {
                var childName = frame.Children.Current;

                if (!visited.Contains(childName))
                {
                    visited.Add(childName);
                    var childNode = CreateTreeNode(childName);
                    frame.Node.Children.Add(childNode);
                    work.Push((childNode,
                               frame.Depth + 1,
                               EnumerateChildren(childName, frame.Depth + 1, maxDepth, childAccessor)));
                }
            }
            else
            {
                work.Pop();
            }
        }

        return root;
    }

    private static IEnumerator<string> EnumerateChildren(
        string name,
        int depth,
        int? maxDepth,
        Func<string, HashSet<string>> childAccessor)
    {
        if (maxDepth.HasValue
            && depth >= maxDepth.Value)
        {
            yield break;
        }

        foreach (var child in childAccessor(name).OrderBy(d => d))
        {
            yield return child;
        }
    }

    private DependencyTreeNode BuildBothTree(
        string rootName,
        int? maxDepth,
        HashSet<string>? highlightSet)
    {

        var root = new DependencyTreeNode
        {
            Name = rootName,
            DependencyCount = GetDirectDependencyCount(rootName),
            DependentCount = GetDirectDependentCount(rootName),
            Children = new()
        };

        var depsRoot = new DependencyTreeNode
        {
            Name = "Dependencies",
            Children = new()
        };
        var depTree = BuildDependencyTree(rootName, maxDepth, highlightSet);
        depsRoot.Children.AddRange(depTree.Children);
        root.Children.Add(depsRoot);

        var dependentsRoot = new DependencyTreeNode
        {
            Name = "Dependents",
            Children = new()
        };
        var dependentTree = BuildDependentTree(rootName, maxDepth, highlightSet);
        dependentsRoot.Children.AddRange(dependentTree.Children);
        root.Children.Add(dependentsRoot);

        return root;
    }
}
