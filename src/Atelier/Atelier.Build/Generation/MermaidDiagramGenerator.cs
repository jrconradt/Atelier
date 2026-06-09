using Atelier.Build.Pipeline;
using Templar.Rendering;
using T = Atelier.Build.Templates.Diagram;

namespace Atelier.Build.Generation;

public class MermaidDiagramGenerator
{
    private readonly BuildContext _context;

    public MermaidDiagramGenerator(BuildContext context)
    {
        _context = context;
    }

    public async Task<string> GenerateAsync(IReadOnlyList<BoutiqueManifest> boutiques)
    {
        Directory.CreateDirectory(_context.DiagramOutputDirectory);

        var diagram = GenerateServiceInteractionDiagram(boutiques);
        var outputPath = Path.Combine(
            _context.DiagramOutputDirectory,
            "service-interactions.mmd");

        await File.WriteAllTextAsync(outputPath, diagram).ConfigureAwait(false);

        var latestPath = Path.Combine(_context.DiagramOutputDirectory, "service-interactions-latest.mmd");
        await File.WriteAllTextAsync(latestPath, diagram).ConfigureAwait(false);

        return outputPath;
    }

    private static string GenerateServiceInteractionDiagram(IReadOnlyList<BoutiqueManifest> boutiques)
    {
        var subgraphs = Sequence.Lines(boutiques.OrderBy(b => b.Name, StringComparer.Ordinal)
            .Select(b => (Compositor)new T.BoutiqueSubgraph
            {
                NodeId = SanitizeNodeId(b.Name),
                Name = b.Name,
                Offerings = Sequence.Lines(b.Offerings.OrderBy(o => o, StringComparer.Ordinal)
                    .Select(o => (Compositor)new T.OfferingNode
                    {
                        NodeId = SanitizeNodeId($"{b.Name}_{o}"),
                        Name = o,
                    })),
            }));

        var edges = Sequence.Lines(boutiques.OrderBy(b => b.Name, StringComparer.Ordinal)
            .SelectMany(b => b.Dependencies.OrderBy(d => d, StringComparer.Ordinal)
                .Select(d => (Compositor)new T.InteractionEdge
            {
                SourceId = SanitizeNodeId(b.Name),
                TargetId = SanitizeNodeId(d),
            })));

        return new T.ServiceInteractions
        {
            Subgraphs = subgraphs,
            Edges = edges,
        }.Render();
    }

    private static string SanitizeNodeId(string name) =>
        name.Replace(".", "_").Replace("-", "_").Replace(" ", "_");
}
