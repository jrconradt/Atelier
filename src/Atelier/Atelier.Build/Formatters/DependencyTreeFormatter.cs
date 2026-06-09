using System.Text.Json;
using Atelier.Build.Commands;
using Atelier.Build.Generation;
using Templar.Rendering;
using T = Atelier.Build.Templates.Diagram;

namespace Atelier.Build.Formatters;

public static class DependencyTreeFormatter
{
    public static string FormatAsAsciiTree(DependencyTreeNode root, HashSet<string>? highlightedNodes = null)
        => RenderAsciiNode(root, prefix: string.Empty, isRoot: true, highlightedNodes).Render() + "\n";

    private static IComposable RenderAsciiNode(
        DependencyTreeNode node,
        string prefix,
        bool isRoot,
        HashSet<string>? highlightedNodes)
    {
        var marker = highlightedNodes?.Contains(node.Name) == true ? "* " : string.Empty;
        var header = new T.AsciiNodeLine
        {
            Prefix = isRoot ? string.Empty : prefix,
            Marker = marker,
            Name = node.Name,
        };

        var childBlocks = node.Children.Select((child, i) =>
        {
            var isLast = i == node.Children.Count - 1;
            var connector = isLast ? "└─ " : "├─ ";
            var continuation = isLast ? "   " : "│  ";
            var childPrefix = isRoot ? string.Empty : prefix;
            return RenderAsciiChildSubtree(
                child, childPrefix + connector, childPrefix + continuation, highlightedNodes);
        });

        return Sequence.Lines(new[] { (IComposable)header }.Concat(childBlocks));
    }

    private enum AsciiPhase
    {
        Enter,
        Exit,
    }

    private record struct AsciiFrame(
        AsciiPhase Phase,
        DependencyTreeNode Node,
        string Prefix,
        string ContinuationPrefix,
        int ChildCount);

    private static IComposable RenderAsciiChildSubtree(
        DependencyTreeNode node,
        string prefix,
        string continuationPrefix,
        HashSet<string>? highlightedNodes)
    {
        var work = new Stack<AsciiFrame>();
        var results = new Stack<IComposable>();

        work.Push(new AsciiFrame(AsciiPhase.Enter,
                                 node,
                                 prefix,
                                 continuationPrefix,
                                 0));

        while (work.Count > 0)
        {
            var frame = work.Pop();

            if (frame.Phase == AsciiPhase.Enter)
            {
                var childCount = frame.Node.Children.Count;
                work.Push(new AsciiFrame(AsciiPhase.Exit,
                                         frame.Node,
                                         frame.Prefix,
                                         frame.ContinuationPrefix,
                                         childCount));

                for (var i = childCount - 1; i >= 0; i--)
                {
                    var isLast = i == childCount - 1;
                    var connector = isLast ? "└─ " : "├─ ";
                    var childContinuation = isLast ? "   " : "│  ";
                    work.Push(new AsciiFrame(AsciiPhase.Enter,
                                             frame.Node.Children[i],
                                             frame.ContinuationPrefix + connector,
                                             frame.ContinuationPrefix + childContinuation,
                                             0));
                }
            }
            else
            {
                var marker = highlightedNodes?.Contains(frame.Node.Name) == true ? "* " : string.Empty;
                var header = new T.AsciiNodeLine
                {
                    Prefix = frame.Prefix,
                    Marker = marker,
                    Name = frame.Node.Name,
                };

                var childBlocks = new IComposable[frame.ChildCount];
                for (var i = frame.ChildCount - 1; i >= 0; i--)
                {
                    childBlocks[i] = results.Pop();
                }

                results.Push(Sequence.Lines(new[] { (IComposable)header }.Concat(childBlocks)));
            }
        }

        return results.Pop();
    }

    public static string FormatAsMermaid(DependencyTreeNode root, HashSet<string>? highlightedNodes = null)
    {
        var nodes = new HashSet<string>();
        var edges = new List<(string from, string to)>();
        CollectNodesAndEdges(root, nodes, edges);

        var nodeBlock = Sequence.Lines(nodes.OrderBy(n => n).Select(n => (Compositor)new T.DependencyNode
            {
                Id = SanitizeNodeId(n),
                Name = n,
                Styling = highlightedNodes?.Contains(n) == true ? ":::highlighted" : string.Empty,
            }));

        var edgeBlock = Sequence.Lines(edges.OrderBy(e => e.from).ThenBy(e => e.to)
            .Select(e => (Compositor)new T.DependencyEdge
                {
                    From = SanitizeNodeId(e.from),
                    To = SanitizeNodeId(e.to),
                }));

        var classDef = highlightedNodes?.Count > 0
            ? "\n\n    classDef highlighted fill:#fff3cd,stroke:#ffc107,stroke-width:3px"
            : string.Empty;

        return new T.DependencyTreeMermaid
        {
            Nodes = nodeBlock,
            Edges = edgeBlock,
            ClassDef = classDef,
        }.Render();
    }

    private static void CollectNodesAndEdges(
        DependencyTreeNode node,
        HashSet<string> nodes,
        List<(string from, string to)> edges)
    {
        var work = new Stack<DependencyTreeNode>();
        work.Push(node);

        while (work.Count > 0)
        {
            var current = work.Pop();

            if (current.Name != "all-subsystems"
                && current.Name != "Dependencies"
                && current.Name != "Dependents")
            {
                nodes.Add(current.Name);
            }

            var nextTargets = new List<DependencyTreeNode>();
            foreach (var child in current.Children)
            {
                if (child.Name != "Dependencies" && child.Name != "Dependents")
                {
                    if (current.Name != "all-subsystems")
                    {
                        edges.Add((child.Name, current.Name));
                    }

                    nextTargets.Add(child);
                }
                else
                {
                    foreach (var grandchild in child.Children)
                    {
                        nextTargets.Add(grandchild);
                    }
                }
            }

            for (var i = nextTargets.Count - 1; i >= 0; i--)
            {
                work.Push(nextTargets[i]);
            }
        }
    }

    private static string SanitizeNodeId(string name)
    {
        return name.Replace("-", "_").Replace(".", "_").Replace(" ", "_");
    }

    public static string FormatAsJson(DependencyTreeNode root)
    {
        return JsonSerializer.Serialize(root, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    public static string FormatAsPlainText(DependencyTreeNode root)
        => RenderPlainText(root, indent: 0).Render();

    private enum PlainPhase
    {
        Enter,
        Exit,
    }

    private record struct PlainFrame(
        PlainPhase Phase,
        DependencyTreeNode Node,
        int Indent,
        bool SkipSelf,
        int ChildCount);

    private static IComposable RenderPlainText(DependencyTreeNode node, int indent)
    {
        var work = new Stack<PlainFrame>();
        var results = new Stack<IComposable>();

        work.Push(new PlainFrame(PlainPhase.Enter,
                                 node,
                                 indent,
                                 false,
                                 0));

        while (work.Count > 0)
        {
            var frame = work.Pop();

            if (frame.Phase == PlainPhase.Enter)
            {
                var skipSelf = frame.Node.Name == "all-subsystems"
                            || frame.Node.Name == "Dependencies"
                            || frame.Node.Name == "Dependents";

                var nextIndent = skipSelf ? frame.Indent : frame.Indent + 1;
                var childCount = frame.Node.Children.Count;

                work.Push(new PlainFrame(PlainPhase.Exit,
                                         frame.Node,
                                         frame.Indent,
                                         skipSelf,
                                         childCount));

                for (var i = childCount - 1; i >= 0; i--)
                {
                    work.Push(new PlainFrame(PlainPhase.Enter,
                                             frame.Node.Children[i],
                                             nextIndent,
                                             false,
                                             0));
                }
            }
            else
            {
                var childItems = new IComposable[frame.ChildCount];
                for (var i = frame.ChildCount - 1; i >= 0; i--)
                {
                    childItems[i] = results.Pop();
                }

                if (frame.SkipSelf)
                {
                    results.Push(Sequence.Lines(childItems));
                }
                else
                {
                    var selfLine = new T.PlainTextNodeLine
                    {
                        Indent = new string('\t', frame.Indent),
                        Name = frame.Node.Name,
                    };

                    results.Push(Sequence.Lines(new[] { (IComposable)selfLine }.Concat(childItems)));
                }
            }
        }

        return results.Pop();
    }
}
