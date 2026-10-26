namespace Atelier.Framework.Attache.Hosting;

/// <summary>
/// A build artifact the boutique host source generator derived from code — the
/// requisite manifest, network zones, network policies. The host writes these out
/// when run with <c>--atelier-artifacts &lt;directory&gt;</c>.
/// </summary>
public sealed class BoutiqueArtifact
{
    public BoutiqueArtifact(string fileName, string content)
    {
        FileName = fileName;
        Content = content;
    }

    internal string FileName { get; }
    internal string Content { get; }
}
