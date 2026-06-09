using YamlDotNet.Serialization;

namespace Atelier.Build.Discovery;

public sealed class BoutiqueIndexSchema
{
    [YamlMember(Alias = "boutiques")]
    public Dictionary<string, string> Boutiques { get; set; } = new(StringComparer.Ordinal);
}
