using Atelier.Build.Pipeline;

namespace Atelier.Build.Generation;

/// <summary>
/// Writes <c>network-policies.yaml</c> at the solution root from the policies each
/// boutique host rendered at compile time, one document per zone.
/// </summary>
public class NetworkPolicyGenerator
{
    private const string DOCUMENT_SEPARATOR = "---\n";

    private readonly BuildContext _context;

    public NetworkPolicyGenerator(BuildContext context)
    {
        _context = context;
    }

    public async Task<string?> GenerateAsync(IEnumerable<string> artifactDirectories)
    {
        var documents = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var directory in artifactDirectories)
        {
            var path = Path.Combine(directory, HostArtifacts.NETWORK_POLICIES);
            if (!File.Exists(path))
            {
                continue;
            }

            var content = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            foreach (var document in content.Split(DOCUMENT_SEPARATOR, StringSplitOptions.RemoveEmptyEntries))
            {
                documents.Add(document);
            }
        }

        if (documents.Count == 0)
        {
            return null;
        }

        var outputPath = Path.Combine(_context.SolutionRoot, "network-policies.yaml");
        await File.WriteAllTextAsync(outputPath, string.Join(DOCUMENT_SEPARATOR, documents)).ConfigureAwait(false);

        return outputPath;
    }
}
