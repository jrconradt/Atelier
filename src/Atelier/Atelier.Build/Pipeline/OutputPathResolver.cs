using Atelier.Build.Discovery;

namespace Atelier.Build.Pipeline;

public sealed class OutputPathResolver
{
    private static readonly char[] PATH_SEGMENT_SEPARATORS = { '/', '\\', ':' };

    public string ResolveBoutiqueOutputDirectory(BoutiqueDefinition definition, string boutiquesDir)
    {
        if (definition.OutputDirectory is not null)
        {
            return definition.OutputDirectory;
        }

        var segment = definition.Name.Replace("atelier-", string.Empty);

        if (segment.Length == 0
            || segment.Contains("..", StringComparison.Ordinal)
            || segment.IndexOfAny(PATH_SEGMENT_SEPARATORS) >= 0)
        {
            throw new InvalidOperationException($"Boutique name '{definition.Name}' is invalid; it must be a single path segment without separators or traversal.");
        }

        var rootedBase = Path.GetFullPath(boutiquesDir);
        var candidate = Path.GetFullPath(Path.Combine(rootedBase, segment));

        if (!string.Equals(candidate, rootedBase, StringComparison.Ordinal)
            && !candidate.StartsWith(rootedBase + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Boutique output directory '{candidate}' escapes the boutiques root '{rootedBase}'.");
        }

        return candidate;
    }
}
